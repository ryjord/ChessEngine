using Engine.Chess.Board;
using Engine.Chess.Core;

namespace Engine.Chess.Tests;

/// <summary>
/// Move-path enumeration against published node counts.
/// </summary>
/// <remarks>
/// These are the tests that matter most. A move generator can be wrong in ways no
/// hand-written test would notice: castling out of check, en passant that uncovers
/// a rook on the fifth rank, a pinned pawn capturing along its pin. Counting every
/// leaf at a fixed depth and comparing against a total the whole chess programming
/// community agrees on catches all of them at once. The positions below are the
/// standard suite, chosen precisely because each one breaks a different naive
/// implementation.
/// </remarks>
public class PerftTests {
    [Theory]
    [InlineData(1, 20L)]
    [InlineData(2, 400L)]
    [InlineData(3, 8_902L)]
    [InlineData(4, 197_281L)]
    [InlineData(5, 4_865_609L)]
    public void StartingPositionMatchesKnownCounts(int depth, long expected) {
        var position = new Position();
        Assert.Equal(expected, Perft.Run(position, depth));
    }

    /// <summary>Kiwipete: dense with castling, pins and promotions all at once.</summary>
    [Theory]
    [InlineData(1, 48L)]
    [InlineData(2, 2_039L)]
    [InlineData(3, 97_862L)]
    [InlineData(4, 4_085_603L)]
    public void KiwipeteMatchesKnownCounts(int depth, long expected) {
        var position = new Position("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
        Assert.Equal(expected, Perft.Run(position, depth));
    }

    /// <summary>Built to expose en passant captures that would expose the king along a rank.</summary>
    [Theory]
    [InlineData(1, 14L)]
    [InlineData(2, 191L)]
    [InlineData(3, 2_812L)]
    [InlineData(4, 43_238L)]
    [InlineData(5, 674_624L)]
    public void EnPassantPinPositionMatchesKnownCounts(int depth, long expected) {
        var position = new Position("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1");
        Assert.Equal(expected, Perft.Run(position, depth));
    }

    /// <summary>Promotion-heavy, including under-promotions that give check.</summary>
    [Theory]
    [InlineData(1, 6L)]
    [InlineData(2, 264L)]
    [InlineData(3, 9_467L)]
    [InlineData(4, 422_333L)]
    public void PromotionPositionMatchesKnownCounts(int depth, long expected) {
        var position = new Position("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1");
        Assert.Equal(expected, Perft.Run(position, depth));
    }

    /// <summary>The same position mirrored: a colour-dependent bug shows up as a mismatch.</summary>
    [Theory]
    [InlineData(3, 9_467L)]
    [InlineData(4, 422_333L)]
    public void MirroredPromotionPositionMatchesKnownCounts(int depth, long expected) {
        var position = new Position("r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1");
        Assert.Equal(expected, Perft.Run(position, depth));
    }

    [Theory]
    [InlineData(1, 44L)]
    [InlineData(2, 1_486L)]
    [InlineData(3, 62_379L)]
    [InlineData(4, 2_103_487L)]
    public void CrampedPositionMatchesKnownCounts(int depth, long expected) {
        var position = new Position("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8");
        Assert.Equal(expected, Perft.Run(position, depth));
    }

    [Theory]
    [InlineData(1, 46L)]
    [InlineData(2, 2_079L)]
    [InlineData(3, 89_890L)]
    [InlineData(4, 3_894_594L)]
    public void MiddlegamePositionMatchesKnownCounts(int depth, long expected) {
        var position = new Position("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10");
        Assert.Equal(expected, Perft.Run(position, depth));
    }

    /// <summary>
    /// Unmaking every move must restore the position exactly. If it does not, perft
    /// can still pass by coincidence while the search silently corrupts the board.
    /// </summary>
    [Theory]
    [InlineData(Position.StartingFen)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1")]
    public void MakeAndUnmakeRestoresThePositionExactly(string fen) {
        var position = new Position(fen);
        string original = position.ToFen();
        ulong originalHash = position.ZobristKey;

        MoveList moves = position.LegalMoves();
        for (int i = 0; i < moves.Count; i++) {
            position.MakeMove(moves[i]);
            position.UnmakeMove();

            Assert.Equal(original, position.ToFen());
            Assert.Equal(originalHash, position.ZobristKey);
        }
    }

    /// <summary>
    /// The incrementally updated hash must agree with one computed from scratch,
    /// or the transposition table will return results for the wrong position.
    /// </summary>
    [Fact]
    public void IncrementalHashMatchesAFullRecomputation() {
        var position = new Position();
        AssertHashesMatch(position, depth: 4);

        static void AssertHashesMatch(Position position, int depth) {
            if (depth == 0) return;

            MoveList moves = position.LegalMoves();
            for (int i = 0; i < moves.Count; i++) {
                position.MakeMove(moves[i]);

                var reloaded = new Position(position.ToFen());
                Assert.Equal(reloaded.ZobristKey, position.ZobristKey);

                AssertHashesMatch(position, depth - 1);
                position.UnmakeMove();
            }
        }
    }
}
