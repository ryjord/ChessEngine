using Engine.Chess.Analysis;
using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Play;

namespace Engine.Chess.Tests;

/// <summary>
/// Bots, the opening book and the post-game review: the layer between the raw
/// engine and the interface.
/// </summary>
public class PlayTests {
    [Fact]
    public void TheOpeningBookAnswersTheStartingPosition() {
        var start = new Position();

        Assert.True(OpeningBook.Default.TryGetMoves(start, out IReadOnlyList<Move> replies));
        Assert.NotEmpty(replies);

        // Every stored move must be legal, which proves the book was parsed and not
        // simply loaded as text.
        MoveList legal = start.LegalMoves();
        foreach (Move move in replies) Assert.True(legal.Contains(move));
    }

    [Fact]
    public void TheOpeningBookCoversMoreThanOneFirstMove() {
        OpeningBook.Default.TryGetMoves(new Position(), out IReadOnlyList<Move> replies);
        Assert.True(replies.Count >= 3, "the book should offer a choice of openings");
    }

    [Fact]
    public void EveryBotProducesLegalMoves() {
        foreach (BotProfile profile in BotProfile.All) {
            var game = new ChessGame();
            var bot = new ChessBot(profile, seed: 1);

            for (int ply = 0; ply < 8 && !game.IsOver; ply++) {
                BotMove choice = bot.ChooseMove(game.Position);
                Assert.False(choice.Move.IsNull, $"{profile.Name} returned no move");
                Assert.True(game.TryMakeMove(choice.Move),
                    $"{profile.Name} returned the illegal move {choice.Move.ToUci()}");
            }
        }
    }

    [Fact]
    public void ASeededBotIsReproducible() {
        var first = new ChessBot(BotProfile.ByName("Club"), seed: 7);
        var second = new ChessBot(BotProfile.ByName("Club"), seed: 7);

        Assert.Equal(
            first.ChooseMove(new Position()).Move,
            second.ChooseMove(new Position()).Move);
    }

    [Fact]
    public void TheStrongestBotPlaysTheBestMoveItFinds() {
        var engine = new ChessBot(BotProfile.ByName("Engine"), seed: 3);
        var position = new Position("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");

        Assert.Equal("a1a8", engine.ChooseMove(position).Move.ToUci());
        Assert.Equal(0, engine.ChooseMove(position).CentipawnsGivenAway);
    }

    [Fact]
    public void EvenTheWeakestBotTakesAMateInOne() {
        // A weak bot should look weak, not broken. Declining mate in one reads as broken.
        var beginner = new ChessBot(BotProfile.ByName("Pawn"), seed: 11);
        var position = new Position("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");

        Assert.Equal("a1a8", beginner.ChooseMove(position).Move.ToUci());
    }

    [Fact]
    public void StrongerBotsGiveAwayLessThanWeakerOnes() {
        Assert.True(BotProfile.ByName("Master").AllowedLoss < BotProfile.ByName("Rookie").AllowedLoss);
        Assert.True(BotProfile.ByName("Master").Depth > BotProfile.ByName("Rookie").Depth);
    }

    [Fact]
    public void TheBotLadderIsOrderedByRating() {
        var elos = BotProfile.All.Select(profile => profile.Elo).ToList();
        Assert.Equal(elos.OrderBy(elo => elo), elos);
    }

    [Fact]
    public void ExpectedScoreFollowsTheEloCurve() {
        BotProfile club = BotProfile.ByName("Club");

        Assert.Equal(0.5, club.ExpectedScoreAgainst(club.Elo), 3);
        Assert.True(club.ExpectedScoreAgainst(club.Elo + 400) < 0.25);
        Assert.True(club.ExpectedScoreAgainst(club.Elo - 400) > 0.75);
    }

    // ---------------------------------------------------------------- review

    [Fact]
    public void ReviewFlagsABlunderAndCreditsTheRefutation() {
        var game = new ChessGame();
        // 2.Bc4 hangs the bishop to the pawn on d5, which is the whole point here.
        foreach (string san in "e4 d5 Bc4 dxc4".Split(' ')) Assert.True(game.TryMakeSanMove(san));

        GameReport report = new GameReview(depth: 6, millisecondsPerMove: 200)
            .Review(new Position(), game.MoveSequence);

        Assert.Equal(4, report.Moves.Count);

        ReviewedMove blunder = report.Moves[2];
        Assert.Equal("Bc4", blunder.San);
        Assert.True(blunder.CentipawnLoss > 200, $"expected a large loss, got {blunder.CentipawnLoss}");
        Assert.True(blunder.Quality is MoveQuality.Blunder or MoveQuality.Mistake, $"was {blunder.Quality}");

        // The side that blundered must come out with the lower accuracy.
        Assert.True(report.White.Accuracy < report.Black.Accuracy);
    }

    [Fact]
    public void ReviewMarksTheOpeningAsBook() {
        var game = new ChessGame();
        foreach (string san in "e4 e5 Nf3 Nc6".Split(' ')) Assert.True(game.TryMakeSanMove(san));

        GameReport report = new GameReview(depth: 5, millisecondsPerMove: 150)
            .Review(new Position(), game.MoveSequence);

        Assert.All(report.Moves, move => Assert.Equal(MoveQuality.Book, move.Quality));
    }

    [Fact]
    public void APerfectMoveScoresFullAccuracy() {
        Assert.Equal(100, WinProbability.MoveAccuracy(60, 60), 0);
        Assert.True(WinProbability.MoveAccuracy(80, 20) < 20);
    }

    [Fact]
    public void ReviewLeavesTheStartingPositionUntouched() {
        var game = new ChessGame();
        foreach (string san in "e4 e5 Nf3".Split(' ')) Assert.True(game.TryMakeSanMove(san));

        var start = new Position();
        string before = start.ToFen();
        new GameReview(depth: 4, millisecondsPerMove: 100).Review(start, game.MoveSequence);

        Assert.Equal(before, start.ToFen());
    }

    // ---------------------------------------------------------------- game state

    [Fact]
    public void ResigningEndsTheGameForTheResigningSide() {
        var game = new ChessGame();
        game.Resign(Color.White);

        Assert.True(game.IsOver);
        Assert.Equal(GameResult.WhiteResigned, game.Result);
        Assert.Equal("0-1", game.Result.ToScoreString());
    }

    [Fact]
    public void FlaggingWithoutMatingMaterialIsADraw() {
        // A bare king cannot mate, so the opponent's flag fall is not a loss.
        var game = new ChessGame("4k3/8/8/8/8/8/8/4K3 w - - 0 1");
        game.DeclareTimeout(Color.Black);

        Assert.Equal(GameResult.DrawByInsufficientMaterial, game.Result);
    }

    [Fact]
    public void FlaggingAgainstMatingMaterialIsALoss() {
        var game = new ChessGame("4k3/8/8/8/8/8/8/3QK3 w - - 0 1");
        game.DeclareTimeout(Color.Black);

        Assert.Equal(GameResult.WhiteWinsOnTime, game.Result);
    }

    [Fact]
    public void PromotionOffersEveryPieceThroughTheGame() {
        var game = new ChessGame("8/4P3/8/8/8/8/8/K6k w - - 0 1");
        IReadOnlyList<Move> promotions = game.MovesBetween(Squares.From(4, 6), Squares.From(4, 7));

        Assert.Equal(4, promotions.Count);
        Assert.All(promotions, move => Assert.True(move.IsPromotion));
    }

    [Fact]
    public void PseudoDestinationsAreAvailableForPremovesOutOfTurn() {
        // It is black's turn, but a white piece still needs candidate squares so the
        // player can queue a premove.
        var position = new Position("4k3/8/8/8/8/8/4P3/4K3 b - - 0 1");
        ulong destinations = position.PseudoDestinationsFrom(Squares.From(4, 1));

        Assert.NotEqual(0UL, destinations);
        Assert.Equal(0UL, position.LegalDestinations(Squares.From(4, 1)));
    }
}
