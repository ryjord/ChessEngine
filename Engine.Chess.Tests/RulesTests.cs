using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Play;

namespace Engine.Chess.Tests;

/// <summary>
/// The rules that perft counts but never names. Perft proves the totals are right;
/// these prove the engine reaches the conclusions a player would expect it to.
/// </summary>
public class RulesTests {
    [Fact]
    public void BackRankMateIsCheckmate() {
        var position = new Position("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");
        position.MakeMove(FindMove(position, "a1a8"));

        Assert.True(position.IsInCheck);
        Assert.Equal(0, position.LegalMoves().Count);
        Assert.Equal(GameResult.WhiteWinsByCheckmate, position.Result());
    }

    [Fact]
    public void KingWithNoLegalMoveAndNoCheckIsStalemate() {
        var position = new Position("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");

        Assert.False(position.IsInCheck);
        Assert.Equal(0, position.LegalMoves().Count);
        Assert.Equal(GameResult.DrawByStalemate, position.Result());
    }

    [Fact]
    public void CastlingThroughAnAttackedSquareIsRejected() {
        // The rook on f8 covers f1, so white may not castle kingside through it.
        var position = new Position("4kr2/8/8/8/8/8/8/4K2R w K - 0 1");
        Assert.DoesNotContain(position.LegalMoves().ToArray(), move => move.Flag == MoveFlag.KingsideCastle);
    }

    [Fact]
    public void CastlingOutOfCheckIsRejected() {
        var position = new Position("4rk2/8/8/8/8/8/8/4K2R w K - 0 1");
        Assert.DoesNotContain(position.LegalMoves().ToArray(), move => move.IsCastle);
    }

    [Fact]
    public void CastlingIsLegalWhenOnlyTheRooksPathIsAttacked() {
        // A rook on b8 attacks b1, which only the rook crosses, so queenside castling stands.
        var position = new Position("1r5k/8/8/8/8/8/8/R3K3 w Q - 0 1");
        Assert.Contains(position.LegalMoves().ToArray(), move => move.Flag == MoveFlag.QueensideCastle);
    }

    [Fact]
    public void CastlingMovesTheRookToTheCorrectSquare() {
        var position = new Position("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1");

        position.MakeMove(FindMove(position, "e1g1"));
        Assert.Equal(Piece.WhiteKing, position.PieceAt(Squares.G1));
        Assert.Equal(Piece.WhiteRook, position.PieceAt(Squares.F1));
        Assert.Equal(Piece.None, position.PieceAt(Squares.H1));

        position.UnmakeMove();
        Assert.Equal(Piece.WhiteRook, position.PieceAt(Squares.H1));
        Assert.Equal(Piece.WhiteKing, position.PieceAt(Squares.E1));
    }

    [Fact]
    public void EnPassantRemovesTheCapturedPawn() {
        var position = new Position("4k3/8/8/8/4pP2/8/8/4K3 b - f3 0 1");
        Move enPassant = FindMove(position, "e4f3");

        Assert.True(enPassant.IsEnPassant);
        position.MakeMove(enPassant);

        Assert.Equal(Piece.BlackPawn, position.PieceAt(Squares.From(5, 2)));
        Assert.Equal(Piece.None, position.PieceAt(Squares.From(5, 3)));
    }

    [Fact]
    public void EnPassantThatWouldExposeTheKingIsRejected() {
        // Taking en passant clears two squares on the fifth rank at once, which
        // would open the h5 rook's line onto the white king on a5.
        var position = new Position("8/8/8/K1pP3r/8/8/8/7k w - c6 0 1");
        Assert.DoesNotContain(position.LegalMoves().ToArray(), move => move.IsEnPassant);
    }

    [Fact]
    public void APinnedPieceMayNotLeaveTheLine() {
        // The knight on e2 is pinned to the king on e1 by the rook on e8.
        var position = new Position("4r2k/8/8/8/8/8/4N3/4K3 w - - 0 1");
        Assert.DoesNotContain(position.LegalMoves().ToArray(), move => move.From == Squares.From(4, 1));
    }

    [Fact]
    public void APinnedPieceMayStillMoveAlongTheLine() {
        // The rook on e2 is pinned but can slide up and down the e-file.
        var position = new Position("4r2k/8/8/8/8/8/4R3/4K3 w - - 0 1");
        Assert.Contains(position.LegalMoves().ToArray(), move => move.From == Squares.From(4, 1));
    }

    [Fact]
    public void PromotionOffersAllFourPieces() {
        var position = new Position("8/4P3/8/8/8/8/8/K6k w - - 0 1");
        var promotions = position.LegalMoves().ToArray()
            .Where(move => move.IsPromotion)
            .Select(move => move.PromotionPiece)
            .ToHashSet();

        Assert.Equal(
            [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight],
            promotions.OrderBy(piece => piece).ToHashSet());
    }

    [Fact]
    public void FiftyMovesWithoutACaptureOrPawnMoveIsADraw() {
        var position = new Position("4k3/8/8/8/8/8/8/R3K3 w - - 99 60");
        position.MakeMove(FindMove(position, "a1a2"));

        Assert.Equal(100, position.HalfmoveClock);
        Assert.Equal(GameResult.DrawByFiftyMoveRule, position.Result());
    }

    [Fact]
    public void RepeatingAPositionThreeTimesIsADraw() {
        var game = new ChessGame("4k3/8/8/8/8/8/8/4K2R w K - 0 1");

        // Shuffling the rook and king returns to the same position every four plies.
        // The first cycle gives up castling rights, so it is not itself a repeat; the
        // draw then lands part way through the third cycle, as soon as any one
        // position has been seen three times, which is why the loop stops on the result
        // rather than asserting a fixed number of moves.
        string[] shuffle = ["Rh2", "Ke7", "Rh1", "Ke8"];
        foreach (string move in shuffle.Concat(shuffle).Concat(shuffle)) {
            if (game.IsOver) break;
            Assert.True(game.TryMakeSanMove(move), $"expected {move} to be legal");
        }

        Assert.Equal(GameResult.DrawByRepetition, game.Result);
        Assert.True(game.History.Count < 12, "the draw should be reached before the shuffle runs out");
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1", true)]           // bare kings
    [InlineData("4k3/8/8/8/8/8/8/3BK3 w - - 0 1", true)]          // king and bishop
    [InlineData("4k3/8/8/8/8/8/8/3NK3 w - - 0 1", true)]          // king and knight
    [InlineData("3bk3/8/8/8/8/8/8/2B1K3 w - - 0 1", true)]        // bishops on one colour (c1, d8)
    [InlineData("2b1k3/8/8/8/8/8/8/2B1K3 w - - 0 1", false)]      // bishops on both colours (c1, c8)
    [InlineData("4k3/8/8/8/8/8/8/3NKN2 w - - 0 1", false)]        // two knights
    [InlineData("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", false)]        // a pawn can promote
    public void InsufficientMaterialIsDetected(string fen, bool expected) {
        Assert.Equal(expected, new Position(fen).IsInsufficientMaterial());
    }

    [Fact]
    public void AnIllegalMoveIsRejectedWithoutChangingThePosition() {
        var game = new ChessGame();
        string before = game.Position.ToFen();

        Assert.False(game.TryMakeMove(new Move(Squares.From(4, 1), Squares.From(4, 4))));
        Assert.Equal(before, game.Position.ToFen());
        Assert.Empty(game.History);
    }

    [Fact]
    public void TakingBackRestoresThePreviousPosition() {
        var game = new ChessGame();
        string before = game.Position.ToFen();

        Assert.True(game.TryMakeSanMove("e4"));
        Assert.True(game.TryUndo());

        Assert.Equal(before, game.Position.ToFen());
        Assert.Empty(game.History);
    }

    /// <summary>Looks up a legal move by its long algebraic form, failing loudly if it is not legal.</summary>
    private static Move FindMove(Position position, string uci) {
        MoveList moves = position.LegalMoves();
        for (int i = 0; i < moves.Count; i++) {
            if (moves[i].ToUci() == uci) return moves[i];
        }
        Assert.Fail($"'{uci}' is not legal in {position.ToFen()}");
        return Move.None;
    }
}
