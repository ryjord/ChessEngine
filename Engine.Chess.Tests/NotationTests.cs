using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Notation;
using Engine.Chess.Play;

namespace Engine.Chess.Tests;

/// <summary>
/// FEN, SAN and PGN. Notation is where an engine meets the rest of the world, so
/// these check both that it reads what other tools write and that what it writes
/// can be read back.
/// </summary>
public class NotationTests {
    [Theory]
    [InlineData(Position.StartingFen)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1")]
    [InlineData("4k3/8/8/8/4pP2/8/8/4K3 b - f3 12 34")]
    public void FenSurvivesARoundTrip(string fen) {
        Assert.Equal(fen, new Position(fen).ToFen());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a fen")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")]              // no side to move
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")] // bad side to move
    [InlineData("8/8/8/8/8/8/8/8 w - - 0 1")]                                // no kings
    [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")] // rank overflows
    public void MalformedFenIsRejected(string fen) {
        Assert.Throws<FormatException>(() => new Position(fen));
    }

    [Fact]
    public void FenWithTheSideNotToMoveInCheckIsRejected() {
        // Black is in check but it is white's turn, which cannot arise in a real game.
        Assert.Throws<FormatException>(() => new Position("4k2R/8/8/8/8/8/8/4K3 w - - 0 1"));
    }

    [Theory]
    [InlineData(Position.StartingFen, "e2e4", "e4")]
    [InlineData(Position.StartingFen, "g1f3", "Nf3")]
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1", "e1g1", "O-O")]
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1", "e1c1", "O-O-O")]
    [InlineData("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", "a1a8", "Ra8#")]
    [InlineData("4k3/8/8/8/8/8/8/4KR2 w - - 0 1", "f1f8", "Rf8+")]
    [InlineData("8/4P3/8/8/8/8/8/K6k w - - 0 1", "e7e8q", "e8=Q")]
    [InlineData("4k3/8/8/3p4/4P3/8/8/4K3 w - - 0 1", "e4d5", "exd5")]
    public void MovesRenderAsExpectedSan(string fen, string uci, string expected) {
        var position = new Position(fen);
        Assert.Equal(expected, San.ToSan(position, FindMove(position, uci)));
    }

    [Fact]
    public void AmbiguousMovesAreDisambiguatedByFile() {
        // Knights on b1 and f3 can both reach d2, so the file distinguishes them.
        var position = new Position("4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1");
        Assert.Equal("Nbd2", San.ToSan(position, FindMove(position, "b1d2")));
        Assert.Equal("Nfd2", San.ToSan(position, FindMove(position, "f3d2")));
    }

    [Fact]
    public void AmbiguousMovesOnOneFileAreDisambiguatedByRank() {
        // Both rooks are on the a-file, so the rank is what tells them apart.
        var position = new Position("R7/8/8/8/8/8/8/R3K2k w - - 0 1");
        Assert.Equal("R8a4", San.ToSan(position, FindMove(position, "a8a4")));
        Assert.Equal("R1a4", San.ToSan(position, FindMove(position, "a1a4")));
    }

    [Fact]
    public void SanParsingIsTheInverseOfSanRendering() {
        var position = new Position();
        foreach (string san in "e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1 b5 Bb3 d6 c3 O-O".Split(' ')) {
            Move parsed = San.Parse(position, san);
            Assert.False(parsed.IsNull, $"'{san}' should parse in {position.ToFen()}");
            Assert.Equal(san, San.ToSan(position, parsed));
            position.MakeMove(parsed);
        }
    }

    [Theory]
    [InlineData("Qxd9")]
    [InlineData("Ke2")]
    [InlineData("O-O")]
    [InlineData("rubbish")]
    public void SanThatMatchesNoLegalMoveReturnsNone(string san) {
        Assert.True(San.Parse(new Position(), san).IsNull);
    }

    [Fact]
    public void ExportedPgnCanBeReadBack() {
        var game = new ChessGame();
        foreach (string san in "e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O".Split(' ')) {
            Assert.True(game.TryMakeSanMove(san));
        }

        string pgn = game.ToPgn("White player", "Black player");
        IReadOnlyList<Move> parsed = Pgn.ParseMoves(pgn, new Position());

        Assert.Equal(game.MoveSequence, parsed);
        Assert.Contains("[White \"White player\"]", pgn);
        Assert.Contains("1. e4 e5", pgn);
    }

    [Fact]
    public void PgnImportIgnoresCommentsAndVariations() {
        const string pgn = """
            [Event "Test"]
            [Result "*"]

            1. e4 {a good move} e5 (1... c5 is the Sicilian) 2. Nf3 ; trailing comment
            2... Nc6 *
            """;

        IReadOnlyList<Move> moves = Pgn.ParseMoves(pgn, new Position());
        Assert.Equal(["e2e4", "e7e5", "g1f3", "b8c6"], moves.Select(move => move.ToUci()));
    }

    [Fact]
    public void PlayedMovesRecordCapturesForTheCapturedStrip() {
        var game = new ChessGame();
        foreach (string san in "e4 d5 exd5".Split(' ')) Assert.True(game.TryMakeSanMove(san));

        Assert.Equal([PieceType.Pawn], game.CapturedBy(Color.White));
        Assert.Empty(game.CapturedBy(Color.Black));
    }

    private static Move FindMove(Position position, string uci) {
        MoveList moves = position.LegalMoves();
        for (int i = 0; i < moves.Count; i++) {
            if (moves[i].ToUci() == uci) return moves[i];
        }
        Assert.Fail($"'{uci}' is not legal in {position.ToFen()}");
        return Move.None;
    }
}
