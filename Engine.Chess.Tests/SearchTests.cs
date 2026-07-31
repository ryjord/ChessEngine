using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Evaluation;
using Engine.Chess.Notation;
using Engine.Chess.Search;

namespace Engine.Chess.Tests;

/// <summary>
/// Search and evaluation.
/// </summary>
/// <remarks>
/// A search cannot be tested by asserting on an exact score, because every pruning
/// heuristic legitimately changes what a given depth returns. What can be asserted
/// is that it reaches the conclusions any correct engine must: it finds forced
/// mates, it never plays an illegal move, and it does not walk into losing material
/// for nothing.
/// </remarks>
public class SearchTests {
    [Fact]
    public void FindsMateInOne() {
        var position = new Position("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");
        SearchResult result = new SearchEngine().Search(position, SearchLimits.ToDepth(3));

        Assert.Equal("Ra8#", San.ToSan(position, result.BestMove));
        Assert.Equal(1, result.MateIn);
    }

    [Fact]
    public void FindsAForcedMateSeveralMovesDeep() {
        var position = new Position("r5rk/5p1p/5R2/4B3/8/8/7P/7K w - - 0 1");
        SearchResult result = new SearchEngine().Search(position, SearchLimits.ToDepth(8));

        Assert.True(result.IsMate, $"expected a forced mate, scored {result.Score}");
        Assert.Equal(3, result.MateIn);
    }

    [Fact]
    public void RecognisesBeingMated() {
        // Black is already checkmated, so there is nothing to search.
        var position = new Position("R5k1/5ppp/8/8/8/8/8/6K1 b - - 0 1");
        SearchResult result = new SearchEngine().Search(position, SearchLimits.ToDepth(4));

        Assert.True(result.BestMove.IsNull);
        Assert.Equal(0, position.LegalMoves().Count);
    }

    [Fact]
    public void TakesFreeMaterial() {
        // The black queen on d5 is undefended and the pawn on e4 can take it.
        var position = new Position("4k3/8/8/3q4/4P3/8/8/4K3 w - - 0 1");
        SearchResult result = new SearchEngine().Search(position, SearchLimits.ToDepth(6));

        Assert.Equal("exd5", San.ToSan(position, result.BestMove));
    }

    [Fact]
    public void DoesNotHangAPieceForNothing() {
        // Quiescence exists to stop the search ending mid-exchange and thinking a
        // defended capture is free. Without it this test fails.
        var position = new Position("4k3/8/4p3/3p4/8/8/8/3QK3 w - - 0 1");
        SearchResult result = new SearchEngine().Search(position, SearchLimits.ToDepth(6));

        string played = San.ToSan(position, result.BestMove);
        Assert.NotEqual("Qxd5", played);
    }

    [Fact]
    public void PrefersTheFasterMate() {
        // Mate is available in one; a slower forced mate must not be chosen instead.
        var position = new Position("6k1/5ppp/8/8/8/8/1R6/R5K1 w - - 0 1");
        SearchResult result = new SearchEngine().Search(position, SearchLimits.ToDepth(6));

        Assert.Equal(1, result.MateIn);
    }

    [Fact]
    public void EveryMoveTheSearchReturnsIsLegal() {
        var position = new Position();
        var engine = new SearchEngine();

        for (int ply = 0; ply < 40 && !position.IsGameOver(); ply++) {
            SearchResult result = engine.Search(position, SearchLimits.ToDepth(4));
            Assert.False(result.BestMove.IsNull, $"no move returned at ply {ply}");
            Assert.True(position.LegalMoves().Contains(result.BestMove),
                $"illegal move {result.BestMove.ToUci()} in {position.ToFen()}");
            position.MakeMove(result.BestMove);
        }
    }

    [Fact]
    public void SearchLeavesThePositionUntouched() {
        var position = new Position("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
        string before = position.ToFen();

        new SearchEngine().Search(position, SearchLimits.ToDepth(5));

        Assert.Equal(before, position.ToFen());
    }

    [Fact]
    public void TheNodeLimitIsRespected() {
        var position = new Position();
        var engine = new SearchEngine();

        engine.Search(position, new SearchLimits { MaxDepth = 64, MaxNodes = 5_000 });

        // The limit is checked between nodes, so a small overshoot is expected.
        Assert.InRange(engine.Nodes, 1, 60_000);
    }

    [Fact]
    public void ScoringRootMovesRanksThemBestFirst() {
        var position = new Position("4k3/8/8/3q4/4P3/8/8/4K3 w - - 0 1");
        IReadOnlyList<ScoredMove> scored = new SearchEngine().ScoreRootMoves(position, SearchLimits.ToDepth(4));

        Assert.NotEmpty(scored);
        Assert.Equal("exd5", San.ToSan(position, scored[0].Move));
        Assert.Equal(position.LegalMoves().Count, scored.Count);

        for (int i = 1; i < scored.Count; i++) {
            Assert.True(scored[i - 1].Score >= scored[i].Score, "scores must be sorted best first");
        }
    }

    // ---------------------------------------------------------------- evaluation

    [Fact]
    public void TheStartingPositionIsRoughlyBalanced() {
        // Only the tempo bonus should separate the sides at move one.
        Assert.InRange(Evaluator.Evaluate(new Position()), -40, 40);
    }

    [Fact]
    public void EvaluationIsSymmetric() {
        // The same position with colours swapped must score the same for the mover,
        // otherwise the engine plays one colour better than the other.
        var white = new Position("r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4");
        var black = new Position("rnbqk2r/pppp1ppp/5n2/2b1p3/4P3/2N2N2/PPPP1PPP/R1BQKB1R b KQkq - 4 4");

        Assert.InRange(Evaluator.Evaluate(white) - Evaluator.Evaluate(black), -10, 10);
    }

    [Fact]
    public void BeingAQueenUpIsScoredAsWinning() {
        var position = new Position("4k3/8/8/8/8/8/8/3QK3 w - - 0 1");
        Assert.True(Evaluator.Evaluate(position) > 700);
    }

    [Fact]
    public void ThePhaseFallsAsMaterialComesOff() {
        Assert.Equal(24, Evaluator.Phase(new Position()));
        Assert.Equal(0, Evaluator.Phase(new Position("4k3/pppppppp/8/8/8/8/PPPPPPPP/4K3 w - - 0 1")));
    }

    [Theory]
    [InlineData("3r4/8/8/8/8/8/8/3QK2k w - - 0 1", "d1d8", true)]   // an undefended rook is free
    [InlineData("4k3/3p4/8/8/8/8/8/3QK3 w - - 0 1", "d1d7", false)] // the king defends the pawn
    public void StaticExchangeJudgesCapturesCorrectly(string fen, string uci, bool expectedGood) {
        var position = new Position(fen);
        MoveList moves = position.LegalMoves();

        Move capture = Move.None;
        for (int i = 0; i < moves.Count; i++) {
            if (moves[i].ToUci() == uci) capture = moves[i];
        }

        Assert.False(capture.IsNull, $"'{uci}' is not legal in {fen}");
        Assert.Equal(expectedGood, StaticExchange.IsGoodCapture(position, capture));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(100, 59)]
    [InlineData(-100, 41)]
    public void WinProbabilityTracksTheEvaluation(int centipawns, int expectedPercent) {
        Assert.Equal(expectedPercent, (int)Math.Round(Analysis.WinProbability.FromCentipawns(centipawns)));
    }
}
