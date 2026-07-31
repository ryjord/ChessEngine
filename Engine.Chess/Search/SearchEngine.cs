using System.Diagnostics;
using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Evaluation;

namespace Engine.Chess.Search;

/// <summary>
/// A negamax alpha-beta search with iterative deepening, a transposition table,
/// quiescence search, null-move pruning and late move reductions.
/// </summary>
/// <remarks>
/// Almost all of the strength here comes from move ordering rather than from
/// searching more nodes: alpha-beta only prunes well when the best move is tried
/// first, so the transposition move, captures ranked by static exchange, killers
/// and the history table all exist to make that happen as often as possible.
/// </remarks>
public sealed class SearchEngine {
    private const int MaxPly = 96;
    private const int KillersPerPly = 2;

    /// <summary>How often to check the clock. Frequent enough to be responsive, rare enough to be cheap.</summary>
    private const int TimeCheckInterval = 2048;

    private readonly TranspositionTable _table;
    private readonly Move[,] _killers = new Move[MaxPly, KillersPerPly];
    private readonly int[,,] _history = new int[2, Squares.Count, Squares.Count];
    private readonly Move[,] _principalVariation = new Move[MaxPly, MaxPly];
    private readonly int[] _principalVariationLength = new int[MaxPly];
    private readonly int[] _staticEval = new int[MaxPly];

    /// <summary>Reduction to apply at [depth, move index], precomputed because it is pure arithmetic.</summary>
    private static readonly int[,] LateMoveReduction = BuildReductionTable();

    private Position _position = new();
    private SearchLimits _limits = new();
    private Stopwatch _clock = new();
    private long _nodes;
    private bool _aborted;
    private CancellationToken _cancellation;

    public SearchEngine(int transpositionTableMegabytes = 16) {
        _table = new TranspositionTable(transpositionTableMegabytes);
    }

    /// <summary>Nodes visited by the most recent search, including quiescence nodes.</summary>
    public long Nodes => _nodes;

    /// <summary>Time spent on the current or most recent search.</summary>
    public int ElapsedMilliseconds => (int)_clock.ElapsedMilliseconds;

    /// <summary>Clears the transposition table, killers and history. Call between unrelated games.</summary>
    public void Reset() {
        _table.Clear();
        Array.Clear(_killers);
        Array.Clear(_history);
    }

    /// <summary>Runs the full iterative deepening loop and returns the deepest completed result.</summary>
    public SearchResult Search(Position position, SearchLimits limits, CancellationToken cancellation = default) {
        SearchResult result = SearchResult.Empty();
        foreach (SearchResult iteration in SearchIterations(position, limits, cancellation)) {
            result = iteration;
        }
        return result;
    }

    /// <summary>
    /// Yields the result of every completed depth. Callers on a single-threaded host
    /// such as WebAssembly can await between iterations to keep the UI alive, and
    /// still have a usable move if they stop early.
    /// </summary>
    public IEnumerable<SearchResult> SearchIterations(
        Position position, SearchLimits limits, CancellationToken cancellation = default) {
        _position = position;
        _limits = limits;
        _cancellation = cancellation;
        _nodes = 0;
        _aborted = false;
        _clock = Stopwatch.StartNew();
        _table.NewSearch();
        AgeHistory();

        MoveList rootMoves = position.LegalMoves();
        if (rootMoves.Count == 0) {
            yield return SearchResult.Empty();
            yield break;
        }

        // Guarantees a legal move even if the first iteration is cut short.
        Move bestMove = rootMoves[0];
        int bestScore = 0;
        int previousScore = 0;

        for (int depth = 1; depth <= Math.Min(limits.MaxDepth, MaxPly - 1); depth++) {
            int score = SearchRoot(depth, previousScore);

            if (_aborted && depth > 1) break;

            bestScore = score;
            previousScore = score;
            if (_principalVariationLength[0] > 0) bestMove = _principalVariation[0, 0];

            yield return new SearchResult {
                BestMove = bestMove,
                Score = bestScore,
                Depth = depth,
                Nodes = _nodes,
                ElapsedMilliseconds = (int)_clock.ElapsedMilliseconds,
                PrincipalVariation = ExtractPrincipalVariation(),
            };

            if (_aborted) break;
            if (limits.StopOnMate && SearchScores.IsMateScore(bestScore)) break;
            // Starting an iteration that cannot finish wastes the remaining budget;
            // each iteration costs roughly twice the last, so this is the cut-off.
            if (HasTimeLimit && _clock.ElapsedMilliseconds * 2 > limits.MaxTimeMilliseconds) break;
        }
    }

    /// <summary>
    /// Scores every legal root move rather than just finding the best one. Used by
    /// the bot to choose a deliberately imperfect move, and by game review to measure
    /// a played move against the alternatives.
    /// </summary>
    /// <remarks>
    /// Every move is searched with a full window, because the point is to know how
    /// good each one actually is. Alpha-beta at the root would only prove that the
    /// rest are worse than the best, which is exactly the information this needs.
    /// Iterative deepening still applies: each pass reorders the moves for the next,
    /// and only fully completed passes are published, so hitting the time limit
    /// yields shallower scores rather than partial ones.
    /// </remarks>
    public IReadOnlyList<ScoredMove> ScoreRootMoves(
        Position position, SearchLimits limits, CancellationToken cancellation = default) {
        IReadOnlyList<ScoredMove> scored = [];
        foreach (IReadOnlyList<ScoredMove> pass in ScoreRootMovesIterations(position, limits, cancellation)) {
            scored = pass;
        }
        return scored;
    }

    /// <summary>
    /// Yields the scores after each completed depth, so a single-threaded host can
    /// await between passes instead of blocking for the whole search.
    /// </summary>
    public IEnumerable<IReadOnlyList<ScoredMove>> ScoreRootMovesIterations(
        Position position, SearchLimits limits, CancellationToken cancellation = default) {
        _position = position;
        _limits = limits;
        _cancellation = cancellation;
        _nodes = 0;
        _aborted = false;
        _clock = Stopwatch.StartNew();
        _table.NewSearch();
        AgeHistory();

        MoveList legal = position.LegalMoves();
        if (legal.Count == 0) {
            yield return [];
            yield break;
        }

        var current = new ScoredMove[legal.Count];
        for (int i = 0; i < legal.Count; i++) current[i] = new ScoredMove(legal[i], 0);

        bool publishedAny = false;
        int maxDepth = Math.Min(Math.Max(1, limits.MaxDepth), MaxPly - 1);

        for (int depth = 1; depth <= maxDepth; depth++) {
            bool finished = true;

            for (int i = 0; i < current.Length; i++) {
                position.MakeMove(current[i].Move);
                int score = -Negamax(depth - 1, 1, -SearchScores.Infinity, SearchScores.Infinity, allowNullMove: true);
                position.UnmakeMove();

                // A move scored after the abort flag was raised holds a placeholder,
                // not a real score, so the whole pass is discarded.
                if (_aborted) {
                    finished = false;
                    break;
                }
                current[i] = new ScoredMove(current[i].Move, score);
            }

            if (!finished) break;

            Array.Sort(current, (left, right) => right.Score.CompareTo(left.Score));
            publishedAny = true;
            yield return (ScoredMove[])current.Clone();

            if (HasTimeLimit && _clock.ElapsedMilliseconds * 2 > limits.MaxTimeMilliseconds) break;
        }

        // Only reachable if even the first pass was cut short, in which case the
        // order the generator produced is the best information available.
        if (!publishedAny) yield return current;
    }

    private bool HasTimeLimit => _limits.MaxTimeMilliseconds > 0;

    /// <summary>
    /// Searches the root with an aspiration window: assuming the score is close to
    /// the last iteration's lets most searches run with a narrow window, and the
    /// window is widened only when that assumption fails.
    /// </summary>
    private int SearchRoot(int depth, int previousScore) {
        if (depth <= 2 || SearchScores.IsMateScore(previousScore)) {
            return Negamax(depth, 0, -SearchScores.Infinity, SearchScores.Infinity, allowNullMove: false);
        }

        int window = 25;
        while (true) {
            int alpha = Math.Max(previousScore - window, -SearchScores.Infinity);
            int beta = Math.Min(previousScore + window, SearchScores.Infinity);

            int score = Negamax(depth, 0, alpha, beta, allowNullMove: false);
            if (_aborted) return score;
            if (score > alpha && score < beta) return score;

            window *= 4;
            if (window > 1200) {
                return Negamax(depth, 0, -SearchScores.Infinity, SearchScores.Infinity, allowNullMove: false);
            }
        }
    }

    private int Negamax(int depth, int ply, int alpha, int beta, bool allowNullMove) {
        _principalVariationLength[ply] = 0;

        if (ShouldStop()) {
            _aborted = true;
            return 0;
        }

        bool isRoot = ply == 0;
        bool isPrincipalVariation = beta - alpha > 1;

        if (!isRoot) {
            // A repetition or a fifty-move draw is worth exactly zero however good
            // the position looks, so this test has to come before anything else.
            if (_position.IsRepetition() || _position.HalfmoveClock >= 100 || _position.IsInsufficientMaterial()) {
                return SearchScores.Draw;
            }

            // Mate distance pruning: if we already have a faster mate, a slower one
            // in this subtree cannot matter.
            alpha = Math.Max(alpha, -SearchScores.Mate + ply);
            beta = Math.Min(beta, SearchScores.Mate - ply - 1);
            if (alpha >= beta) return alpha;
        }

        bool inCheck = _position.IsInCheck;
        // Being in check means the position is not quiet, so the search is extended
        // rather than handed to a quiescence search that cannot resolve it.
        if (inCheck && depth < MaxPly - 1) depth++;

        if (depth <= 0) return Quiescence(ply, alpha, beta);

        _nodes++;

        Move transpositionMove = Move.None;
        if (!isRoot &&
            _table.Probe(_position.ZobristKey, depth, ply, alpha, beta, out int tableScore, out transpositionMove)) {
            if (!isPrincipalVariation) return tableScore;
        } else if (isRoot) {
            transpositionMove = _table.ProbeMove(_position.ZobristKey);
        }

        int staticEval = inCheck ? -SearchScores.Infinity : Evaluator.Evaluate(_position);
        _staticEval[ply] = staticEval;

        // Whether the position is getting better or worse for us tells the pruning
        // heuristics how much to trust the static evaluation.
        bool improving = !inCheck && ply >= 2 && staticEval > _staticEval[ply - 2];

        if (!isPrincipalVariation && !inCheck && !SearchScores.IsMateScore(beta)) {
            // Reverse futility: a position this far above beta will almost certainly
            // still be above beta after a quiet move.
            int margin = 85 * depth - (improving ? 40 : 0);
            if (depth <= 6 && staticEval - margin >= beta) return staticEval;

            // Null move: give the opponent a free move. If we are still winning, the
            // real move would have been at least as good. Disabled in pawn endgames,
            // where zugzwang makes "doing nothing" a genuinely bad option.
            if (allowNullMove && depth >= 3 && staticEval >= beta && !_position.HasOnlyPawns(_position.SideToMove)) {
                int reduction = 3 + (depth / 6);
                _position.MakeNullMove();
                int nullScore = -Negamax(depth - reduction, ply + 1, -beta, -beta + 1, allowNullMove: false);
                _position.UnmakeNullMove();

                if (nullScore >= beta) return SearchScores.IsMateScore(nullScore) ? beta : nullScore;
            }
        }

        MoveList moves = default;
        MoveGenerator.Generate(_position, ref moves);

        if (moves.Count == 0) return inCheck ? -SearchScores.Mate + ply : SearchScores.Draw;

        Span<int> scores = stackalloc int[moves.Count];
        ScoreMoves(ref moves, scores, transpositionMove, ply);

        Move bestMove = Move.None;
        int bestScore = -SearchScores.Infinity;
        ScoreBound bound = ScoreBound.Upper;
        int quietsTried = 0;

        for (int i = 0; i < moves.Count; i++) {
            SelectNextMove(ref moves, scores, i);
            Move move = moves[i];
            bool isQuiet = !move.IsCapture && !move.IsPromotion;

            // Late move pruning: deep in a move list of quiet moves at low depth,
            // the remaining moves are overwhelmingly unlikely to be best.
            if (!isPrincipalVariation && !inCheck && isQuiet && depth <= 4 &&
                bestScore > -SearchScores.MateThreshold &&
                quietsTried >= 4 + (depth * depth * (improving ? 2 : 1))) {
                continue;
            }

            if (isQuiet) quietsTried++;

            _position.MakeMove(move);

            int score;
            if (i == 0) {
                score = -Negamax(depth - 1, ply + 1, -beta, -alpha, allowNullMove: true);
            } else {
                // Late move reduction: search unpromising moves shallower, and only
                // pay for a full-depth search if the reduced one looks like it beat alpha.
                int reduction = 0;
                if (depth >= 3 && i >= 3 && isQuiet && !inCheck) {
                    reduction = LateMoveReduction[Math.Min(depth, 63), Math.Min(i, 63)];
                    if (isPrincipalVariation) reduction--;
                    if (!improving) reduction++;
                    reduction = Math.Clamp(reduction, 0, depth - 2);
                }

                // Principal variation search: after the first move, prove the rest are
                // worse with a null window, which is far cheaper than scoring them.
                score = -Negamax(depth - 1 - reduction, ply + 1, -alpha - 1, -alpha, allowNullMove: true);

                if (score > alpha && reduction > 0) {
                    score = -Negamax(depth - 1, ply + 1, -alpha - 1, -alpha, allowNullMove: true);
                }
                if (score > alpha && score < beta) {
                    score = -Negamax(depth - 1, ply + 1, -beta, -alpha, allowNullMove: true);
                }
            }

            _position.UnmakeMove();

            if (_aborted) return bestScore > -SearchScores.Infinity ? bestScore : 0;

            if (score <= bestScore) continue;

            bestScore = score;
            bestMove = move;

            if (score <= alpha) continue;

            alpha = score;
            bound = ScoreBound.Exact;
            RecordPrincipalVariation(ply, move);

            if (score < beta) continue;

            // Beta cutoff. Remember why, so the same refutation is tried first next time.
            if (isQuiet) {
                RememberKiller(ply, move);
                RewardHistory(move, depth);
            }
            bound = ScoreBound.Lower;
            break;
        }

        if (!_aborted) _table.Store(_position.ZobristKey, depth, ply, bestScore, bound, bestMove);
        return bestScore;
    }

    /// <summary>
    /// Searches only forcing moves until the position is quiet. Stopping the main
    /// search mid-exchange would evaluate a board where a queen is hanging as if
    /// nothing were wrong, which is the single largest source of tactical blunders.
    /// </summary>
    private int Quiescence(int ply, int alpha, int beta) {
        if (ShouldStop()) {
            _aborted = true;
            return 0;
        }

        _nodes++;
        if (ply >= MaxPly - 1) return Evaluator.Evaluate(_position);

        // Standing pat: we are never forced to capture, so the static score is a floor.
        int standPat = Evaluator.Evaluate(_position);
        if (standPat >= beta) return standPat;
        if (standPat > alpha) alpha = standPat;

        MoveList moves = default;
        MoveGenerator.GenerateCaptures(_position, ref moves);
        if (moves.Count == 0) return standPat;

        Span<int> scores = stackalloc int[moves.Count];
        for (int i = 0; i < moves.Count; i++) scores[i] = StaticExchange.MvvLva(_position, moves[i]);

        int bestScore = standPat;
        for (int i = 0; i < moves.Count; i++) {
            SelectNextMove(ref moves, scores, i);
            Move move = moves[i];

            // Delta pruning: a capture that cannot lift the score near alpha even
            // with a generous margin is not worth searching.
            if (!move.IsPromotion) {
                int captured = move.IsEnPassant
                    ? Evaluator.PieceValue[(int)PieceType.Pawn]
                    : Evaluator.PieceValue[(int)_position.PieceAt(move.To).TypeOf()];
                if (standPat + captured + 200 < alpha) continue;
                if (!StaticExchange.IsGoodCapture(_position, move)) continue;
            }

            _position.MakeMove(move);
            int score = -Quiescence(ply + 1, -beta, -alpha);
            _position.UnmakeMove();

            if (_aborted) return bestScore;
            if (score <= bestScore) continue;

            bestScore = score;
            if (score > alpha) alpha = score;
            if (score >= beta) break;
        }
        return bestScore;
    }

    // ---------------------------------------------------------------- move ordering

    private void ScoreMoves(ref MoveList moves, Span<int> scores, Move transpositionMove, int ply) {
        const int TranspositionBonus = 10_000_000;
        const int GoodCaptureBonus = 8_000_000;
        const int PromotionBonus = 7_000_000;
        const int KillerBonus = 6_000_000;
        const int LosingCaptureBonus = -1_000_000;

        int side = (int)_position.SideToMove;

        for (int i = 0; i < moves.Count; i++) {
            Move move = moves[i];

            if (move == transpositionMove) {
                scores[i] = TranspositionBonus;
            } else if (move.IsPromotion) {
                scores[i] = PromotionBonus + Evaluator.PieceValue[(int)move.PromotionPiece];
            } else if (move.IsCapture) {
                int mvvLva = StaticExchange.MvvLva(_position, move);
                scores[i] = StaticExchange.IsGoodCapture(_position, move)
                    ? GoodCaptureBonus + mvvLva
                    : LosingCaptureBonus + mvvLva;
            } else if (move == _killers[ply, 0]) {
                scores[i] = KillerBonus + 1;
            } else if (move == _killers[ply, 1]) {
                scores[i] = KillerBonus;
            } else {
                scores[i] = _history[side, move.From, move.To];
            }
        }
    }

    /// <summary>
    /// Moves the best remaining candidate into position <paramref name="index"/>.
    /// Selecting lazily beats sorting the whole list, because a beta cutoff usually
    /// arrives within the first few moves and the rest are never looked at.
    /// </summary>
    private static void SelectNextMove(ref MoveList moves, Span<int> scores, int index) {
        int best = index;
        for (int i = index + 1; i < moves.Count; i++) {
            if (scores[i] > scores[best]) best = i;
        }
        if (best == index) return;

        moves.Swap(index, best);
        (scores[index], scores[best]) = (scores[best], scores[index]);
    }

    private void RememberKiller(int ply, Move move) {
        if (_killers[ply, 0] == move) return;
        _killers[ply, 1] = _killers[ply, 0];
        _killers[ply, 0] = move;
    }

    private void RewardHistory(Move move, int depth) {
        int side = (int)_position.SideToMove;
        ref int entry = ref _history[side, move.From, move.To];
        entry += depth * depth;
        // Keep the table bounded so old cutoffs cannot dominate new evidence forever.
        if (entry > 1 << 20) HalveHistory();
    }

    private void HalveHistory() {
        for (int side = 0; side < 2; side++) {
            for (int from = 0; from < Squares.Count; from++) {
                for (int to = 0; to < Squares.Count; to++) _history[side, from, to] /= 2;
            }
        }
    }

    /// <summary>Decays history between searches so the previous move's ordering fades rather than misleads.</summary>
    private void AgeHistory() {
        for (int side = 0; side < 2; side++) {
            for (int from = 0; from < Squares.Count; from++) {
                for (int to = 0; to < Squares.Count; to++) _history[side, from, to] /= 4;
            }
        }
        Array.Clear(_killers);
    }

    // ---------------------------------------------------------------- bookkeeping

    private void RecordPrincipalVariation(int ply, Move move) {
        _principalVariation[ply, 0] = move;
        int childLength = _principalVariationLength[ply + 1];
        for (int i = 0; i < childLength; i++) {
            _principalVariation[ply, i + 1] = _principalVariation[ply + 1, i];
        }
        _principalVariationLength[ply] = childLength + 1;
    }

    private IReadOnlyList<Move> ExtractPrincipalVariation() {
        var line = new Move[_principalVariationLength[0]];
        for (int i = 0; i < line.Length; i++) line[i] = _principalVariation[0, i];
        return line;
    }

    private bool ShouldStop() {
        if (_aborted) return true;
        if (_nodes >= _limits.MaxNodes) return true;
        if ((_nodes & (TimeCheckInterval - 1)) != 0) return false;
        if (_cancellation.IsCancellationRequested) return true;
        return HasTimeLimit && _clock.ElapsedMilliseconds >= _limits.MaxTimeMilliseconds;
    }

    private static int[,] BuildReductionTable() {
        var table = new int[64, 64];
        for (int depth = 1; depth < 64; depth++) {
            for (int moveIndex = 1; moveIndex < 64; moveIndex++) {
                table[depth, moveIndex] = (int)(0.75 + (Math.Log(depth) * Math.Log(moveIndex) / 2.25));
            }
        }
        return table;
    }
}

/// <summary>A root move together with the score the search gave it.</summary>
public readonly record struct ScoredMove(Move Move, int Score);
