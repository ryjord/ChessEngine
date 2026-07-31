using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Notation;
using Engine.Chess.Play;
using Engine.Chess.Search;

namespace Engine.Chess.Analysis;

/// <summary>The verdict on one played move.</summary>
public sealed record ReviewedMove {
    public required int Ply { get; init; }

    public required Color Side { get; init; }

    public required Move Move { get; init; }

    public required string San { get; init; }

    /// <summary>Evaluation before the move, in centipawns from white's point of view.</summary>
    public required int ScoreBefore { get; init; }

    /// <summary>Evaluation after the move, in centipawns from white's point of view.</summary>
    public required int ScoreAfter { get; init; }

    /// <summary>The engine's preferred move in this position.</summary>
    public required Move BestMove { get; init; }

    public required string BestMoveSan { get; init; }

    public required MoveQuality Quality { get; init; }

    /// <summary>Expected-score points given away by this move, on a 0 to 100 scale.</summary>
    public required double WinPercentLost { get; init; }

    public required double Accuracy { get; init; }

    /// <summary>Centipawns lost against the best move, from the mover's point of view.</summary>
    public required int CentipawnLoss { get; init; }

    public bool MatchedBestMove => Move == BestMove;
}

/// <summary>Per-player totals for a reviewed game.</summary>
public sealed record PlayerReport {
    public required Color Side { get; init; }

    /// <summary>Mean move accuracy across the player's moves, from 0 to 100.</summary>
    public required double Accuracy { get; init; }

    /// <summary>Mean centipawn loss, the standard measure of how cleanly someone played.</summary>
    public required double AverageCentipawnLoss { get; init; }

    public required IReadOnlyDictionary<MoveQuality, int> Counts { get; init; }

    /// <summary>Playing strength implied by the accuracy achieved. An estimate, not a rating.</summary>
    public required int EstimatedElo { get; init; }

    public int CountOf(MoveQuality quality) => Counts.TryGetValue(quality, out int count) ? count : 0;
}

/// <summary>The finished review of a whole game.</summary>
public sealed record GameReport {
    public required IReadOnlyList<ReviewedMove> Moves { get; init; }

    public required PlayerReport White { get; init; }

    public required PlayerReport Black { get; init; }

    public PlayerReport For(Color side) => side == Color.White ? White : Black;
}

/// <summary>
/// Replays a finished game and judges every move: what the engine would have
/// played, how much the played move cost, and what that adds up to as an accuracy
/// score for each player.
/// </summary>
/// <remarks>
/// Each position is scored once, with every legal root move given a real score
/// rather than the bound that alpha-beta would leave behind. That single pass
/// supplies everything the classifier needs: the best move, the played move's own
/// score, and the gap to the second best, which is what distinguishes a move that
/// was merely correct from one that was the only thing holding the position
/// together. Scoring the position before and after separately would be both slower
/// and less consistent, because the two searches can disagree.
/// </remarks>
public sealed class GameReview {
    /// <summary>Gap to the second-best move above which a move counts as critical.</summary>
    private const int GreatMoveMargin = 150;

    /// <summary>Material the opponent must be able to win for a move to count as a sacrifice.</summary>
    private const int SacrificeThreshold = 200;

    private readonly SearchEngine _engine;
    private readonly int _depth;
    private readonly int _millisecondsPerMove;

    public GameReview(int depth = 9, int millisecondsPerMove = 250, int transpositionTableMegabytes = 32) {
        _engine = new SearchEngine(transpositionTableMegabytes);
        _depth = depth;
        _millisecondsPerMove = millisecondsPerMove;
    }

    /// <summary>Total moves a review of this game will examine, for progress reporting.</summary>
    public static int StepCount(IReadOnlyList<Move> moves) => moves.Count;

    /// <summary>
    /// Reviews the moves one at a time, yielding after each so a single-threaded
    /// host can show progress instead of freezing. Pass the collected results to
    /// <see cref="Summarise"/> to produce the final report.
    /// </summary>
    public IEnumerable<ReviewedMove> ReviewIncrementally(
        Position start, IReadOnlyList<Move> moves, CancellationToken cancellation = default) {
        var position = start.Clone();
        var limits = new SearchLimits { MaxDepth = _depth, MaxTimeMilliseconds = _millisecondsPerMove };
        OpeningBook book = OpeningBook.Default;

        for (int ply = 0; ply < moves.Count; ply++) {
            if (cancellation.IsCancellationRequested) yield break;

            Move played = moves[ply];
            Color side = position.SideToMove;
            bool inBook = book.TryGetMoves(position, out IReadOnlyList<Move> bookMoves) && bookMoves.Contains(played);
            string san = San.ToSan(position, played);

            IReadOnlyList<ScoredMove> candidates = _engine.ScoreRootMoves(position, limits, cancellation);
            if (candidates.Count == 0) yield break;

            ScoredMove best = candidates[0];
            int playedScore = ScoreOf(candidates, played, fallback: best.Score);
            int secondBestScore = candidates.Count > 1 ? candidates[1].Score : int.MinValue;

            bool sacrificed = IsSacrifice(position, played);
            bool critical = candidates.Count > 1 && best.Score - secondBestScore >= GreatMoveMargin;

            double beforePercent = WinProbability.FromCentipawns(best.Score);
            double afterPercent = WinProbability.FromCentipawns(playedScore);

            var reviewed = new ReviewedMove {
                Ply = ply,
                Side = side,
                Move = played,
                San = san,
                ScoreBefore = ToWhite(side, best.Score),
                ScoreAfter = ToWhite(side, playedScore),
                BestMove = best.Move,
                BestMoveSan = best.Move.IsNull ? string.Empty : San.ToSan(position, best.Move),
                Quality = Classify(played, best.Move, inBook, beforePercent - afterPercent,
                                   best.Score, playedScore, sacrificed, critical),
                WinPercentLost = Math.Max(0, beforePercent - afterPercent),
                Accuracy = WinProbability.MoveAccuracy(beforePercent, afterPercent),
                CentipawnLoss = Math.Max(0, best.Score - playedScore),
            };

            position.MakeMove(played);
            yield return reviewed;
        }
    }

    public GameReport Review(
        Position start, IReadOnlyList<Move> moves, CancellationToken cancellation = default) =>
        Summarise(ReviewIncrementally(start, moves, cancellation).ToList());

    public static GameReport Summarise(IReadOnlyList<ReviewedMove> reviewed) => new() {
        Moves = reviewed,
        White = BuildPlayerReport(reviewed, Color.White),
        Black = BuildPlayerReport(reviewed, Color.Black),
    };

    private static int ScoreOf(IReadOnlyList<ScoredMove> candidates, Move move, int fallback) {
        foreach (ScoredMove candidate in candidates) {
            if (candidate.Move == move) return candidate.Score;
        }
        // Only reachable if the search was cut short before reaching this move.
        return fallback;
    }

    private static PlayerReport BuildPlayerReport(IReadOnlyList<ReviewedMove> reviewed, Color side) {
        List<ReviewedMove> ours = reviewed.Where(move => move.Side == side).ToList();

        var counts = new Dictionary<MoveQuality, int>();
        foreach (ReviewedMove move in ours) {
            counts[move.Quality] = counts.GetValueOrDefault(move.Quality) + 1;
        }

        double accuracy = ours.Count == 0 ? 100 : ours.Average(move => move.Accuracy);
        double averageLoss = ours.Count == 0 ? 0 : ours.Average(move => move.CentipawnLoss);

        return new PlayerReport {
            Side = side,
            Accuracy = accuracy,
            AverageCentipawnLoss = averageLoss,
            Counts = counts,
            EstimatedElo = EstimateElo(accuracy),
        };
    }

    /// <summary>
    /// A rough rating from accuracy, calibrated so that 60 percent lands near a
    /// beginner and 95 percent near a strong club player.
    /// </summary>
    /// <remarks>
    /// Deliberately coarse and clamped. Accuracy over a single game is noisy, and
    /// short or forced games inflate it because there are fewer chances to go wrong,
    /// so this is labelled an estimate everywhere it is shown.
    /// </remarks>
    private static int EstimateElo(double accuracy) =>
        Math.Clamp((int)Math.Round((accuracy - 50) * 44), 250, 2800);

    private static MoveQuality Classify(
        Move played, Move best, bool inBook, double winPercentLost,
        int bestScore, int playedScore, bool sacrificed, bool critical) {
        if (inBook) return MoveQuality.Book;

        // Walking away from a forced mate is its own category: the centipawn loss is
        // enormous but the resulting position may still be perfectly good.
        if (SearchScores.IsMateScore(bestScore) && bestScore > 0 && !SearchScores.IsMateScore(playedScore)) {
            return MoveQuality.Miss;
        }

        if (played == best) {
            if (sacrificed && playedScore > -50) return MoveQuality.Brilliant;
            if (critical) return MoveQuality.Great;
            return MoveQuality.Best;
        }

        return Math.Max(0, winPercentLost) switch {
            < 2 => MoveQuality.Excellent,
            < 5 => MoveQuality.Good,
            < 10 => MoveQuality.Inaccuracy,
            < 20 => MoveQuality.Mistake,
            _ => MoveQuality.Blunder,
        };
    }

    /// <summary>
    /// True when the move deliberately allows the opponent to win material: either
    /// it is a capture that loses the exchange, or it leaves something hanging that
    /// the opponent can take profitably. Paired with the engine still preferring the
    /// move, that is what makes a sacrifice brilliant rather than careless.
    /// </summary>
    private static bool IsSacrifice(Position position, Move move) {
        if (move.IsCapture) return StaticExchange.Evaluate(position, move) <= -100;

        position.MakeMove(move);
        MoveList replies = default;
        MoveGenerator.GenerateCaptures(position, ref replies);

        int bestGain = 0;
        for (int i = 0; i < replies.Count; i++) {
            bestGain = Math.Max(bestGain, StaticExchange.Evaluate(position, replies[i]));
        }
        position.UnmakeMove();

        return bestGain >= SacrificeThreshold;
    }

    private static int ToWhite(Color side, int score) => side == Color.White ? score : -score;
}
