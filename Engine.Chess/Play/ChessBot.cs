using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Search;

namespace Engine.Chess.Play;

/// <summary>The move a bot chose, plus enough detail for the interface to explain the decision.</summary>
public sealed record BotMove {
    public required Move Move { get; init; }

    /// <summary>Evaluation after the chosen move, in centipawns from white's point of view.</summary>
    public required int Score { get; init; }

    public required int Depth { get; init; }

    public required long Nodes { get; init; }

    public required int ElapsedMilliseconds { get; init; }

    public required bool FromOpeningBook { get; init; }

    /// <summary>How much worse the chosen move was than the best one found. Zero when the bot played best.</summary>
    public required int CentipawnsGivenAway { get; init; }

    public required IReadOnlyList<Move> PrincipalVariation { get; init; }

    public static BotMove None => new() {
        Move = Move.None,
        Score = 0,
        Depth = 0,
        Nodes = 0,
        ElapsedMilliseconds = 0,
        FromOpeningBook = false,
        CentipawnsGivenAway = 0,
        PrincipalVariation = [],
    };
}

/// <summary>
/// Turns a <see cref="BotProfile"/> into actual moves: consults the opening book,
/// runs the search under the profile's limits, then applies the profile's tolerance
/// for imperfection when choosing between the candidates.
/// </summary>
public sealed class ChessBot {
    private readonly SearchEngine _engine;
    private readonly Random _random;

    /// <param name="seed">Fixing the seed makes a bot's choices reproducible, which tests rely on.</param>
    public ChessBot(BotProfile profile, int? seed = null, int transpositionTableMegabytes = 16) {
        Profile = profile;
        _engine = new SearchEngine(transpositionTableMegabytes);
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public BotProfile Profile { get; set; }

    public void Reset() => _engine.Reset();

    /// <summary>
    /// A bot with no handicap always plays the move it found, so it can take the
    /// ordinary alpha-beta path instead of paying to score every root move.
    /// </summary>
    private bool PlaysBestMove => Profile.AllowedLoss <= 0 || Profile.MistakeChance <= 0;

    /// <summary>
    /// Chooses a move, pausing between search depths so a single-threaded host stays
    /// responsive and can report progress. The pauses cost a little speed; a frozen
    /// interface costs more.
    /// </summary>
    public async Task<BotMove> ChooseMoveAsync(
        Position position, Action<SearchProgress>? onProgress = null, CancellationToken cancellation = default) {
        BotMove? book = TryOpeningBook(position);
        if (book is not null) {
            // Without a pause a book move lands instantly, which reads as a glitch.
            await Task.Delay(200, cancellation).ConfigureAwait(false);
            return book;
        }

        SearchLimits limits = BuildLimits();

        if (PlaysBestMove) {
            SearchResult best = SearchResult.Empty();
            foreach (SearchResult iteration in _engine.SearchIterations(position, limits, cancellation)) {
                best = iteration;
                onProgress?.Invoke(new SearchProgress {
                    Depth = iteration.Depth,
                    Nodes = iteration.Nodes,
                    ElapsedMilliseconds = iteration.ElapsedMilliseconds,
                    Score = iteration.Score,
                    PrincipalVariation = iteration.PrincipalVariation,
                });
                await Task.Yield();
            }
            return FromSearch(position, best);
        }

        IReadOnlyList<ScoredMove> candidates = [];
        int depth = 0;
        foreach (IReadOnlyList<ScoredMove> pass in _engine.ScoreRootMovesIterations(position, limits, cancellation)) {
            candidates = pass;
            depth++;
            onProgress?.Invoke(new SearchProgress {
                Depth = depth,
                Nodes = _engine.Nodes,
                ElapsedMilliseconds = _engine.ElapsedMilliseconds,
                Score = pass.Count > 0 ? pass[0].Score : null,
                PrincipalVariation = pass.Count > 0 ? [pass[0].Move] : [],
            });
            await Task.Yield();
        }

        return FromCandidates(position, candidates, depth);
    }

    /// <summary>Blocking equivalent of <see cref="ChooseMoveAsync"/>, for tests and headless play.</summary>
    public BotMove ChooseMove(Position position, CancellationToken cancellation = default) {
        BotMove? book = TryOpeningBook(position);
        if (book is not null) return book;

        SearchLimits limits = BuildLimits();

        if (PlaysBestMove) return FromSearch(position, _engine.Search(position, limits, cancellation));

        IReadOnlyList<ScoredMove> candidates = _engine.ScoreRootMoves(position, limits, cancellation);
        return FromCandidates(position, candidates, Profile.Depth);
    }

    private SearchLimits BuildLimits() => new() {
        MaxDepth = Profile.Depth,
        MaxTimeMilliseconds = Profile.ThinkTimeMilliseconds,
    };

    private BotMove? TryOpeningBook(Position position) {
        if (!Profile.UseOpeningBook) return null;

        Move move = OpeningBook.Default.Choose(position, _random);
        if (move.IsNull) return null;

        return BotMove.None with {
            Move = move,
            FromOpeningBook = true,
            PrincipalVariation = [move],
        };
    }

    private static BotMove FromSearch(Position position, SearchResult result) => new() {
        Move = result.BestMove,
        Score = ToWhitePerspective(position, result.Score),
        Depth = result.Depth,
        Nodes = result.Nodes,
        ElapsedMilliseconds = result.ElapsedMilliseconds,
        FromOpeningBook = false,
        CentipawnsGivenAway = 0,
        PrincipalVariation = result.PrincipalVariation,
    };

    private BotMove FromCandidates(Position position, IReadOnlyList<ScoredMove> candidates, int depth) {
        if (candidates.Count == 0) return BotMove.None;

        ScoredMove best = candidates[0];
        ScoredMove chosen = Choose(candidates, best);

        return new BotMove {
            Move = chosen.Move,
            Score = ToWhitePerspective(position, chosen.Score),
            Depth = depth,
            Nodes = _engine.Nodes,
            ElapsedMilliseconds = _engine.ElapsedMilliseconds,
            FromOpeningBook = false,
            CentipawnsGivenAway = Math.Max(0, best.Score - chosen.Score),
            PrincipalVariation = [chosen.Move],
        };
    }

    /// <summary>
    /// Picks among the moves within the profile's tolerance of the best. The weighting
    /// keeps near-best moves far more likely than bad ones, so a weak bot still plays
    /// recognisable chess rather than noise.
    /// </summary>
    private ScoredMove Choose(IReadOnlyList<ScoredMove> candidates, ScoredMove best) {
        if (_random.NextDouble() >= Profile.MistakeChance) return best;

        // Never walk away from a forced mate in either direction: a bot that declines
        // mate in one reads as broken rather than weak.
        if (SearchScores.IsMateScore(best.Score)) return best;

        int floor = best.Score - Profile.AllowedLoss;
        var weights = new double[candidates.Count];
        double totalWeight = 0;

        for (int i = 0; i < candidates.Count; i++) {
            // Candidates are sorted best first, so the first move below the floor
            // ends the eligible set.
            if (candidates[i].Score < floor) break;
            weights[i] = 1.0 / (1.0 + ((best.Score - candidates[i].Score) / 50.0));
            totalWeight += weights[i];
        }

        if (totalWeight <= 0) return best;

        double target = _random.NextDouble() * totalWeight;
        for (int i = 0; i < candidates.Count; i++) {
            target -= weights[i];
            if (target <= 0) return candidates[i];
        }
        return best;
    }

    /// <summary>Search scores are relative to the side to move; the interface wants a fixed orientation.</summary>
    private static int ToWhitePerspective(Position position, int score) =>
        position.SideToMove == Color.White ? score : -score;
}
