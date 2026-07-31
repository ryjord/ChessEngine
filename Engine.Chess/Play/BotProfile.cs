namespace Engine.Chess.Play;

/// <summary>
/// A configured opponent. Strength is limited in two ways that together produce
/// something that feels like a human of that rating rather than a crippled engine.
/// </summary>
/// <remarks>
/// Depth alone is a poor handicap: a shallow engine still never hangs a piece to a
/// two-move tactic, so it feels alien rather than weak. Pairing a depth limit with
/// a tolerance for playing a move that is measurably worse than the best reproduces
/// the actual signature of a weaker player, which is finding good moves most of the
/// time and missing them the rest.
/// </remarks>
public sealed record BotProfile {
    public required string Name { get; init; }

    /// <summary>Approximate playing strength, used for labelling and for expected-score maths.</summary>
    public required int Elo { get; init; }

    /// <summary>A one-line description of how this bot plays, shown in the opponent picker.</summary>
    public required string Style { get; init; }

    /// <summary>Deepest iteration the bot is allowed to complete.</summary>
    public required int Depth { get; init; }

    /// <summary>Thinking budget in milliseconds. Also caps how long the interface stays busy.</summary>
    public required int ThinkTimeMilliseconds { get; init; }

    /// <summary>
    /// How many centipawns worse than the best move this bot will tolerate. Zero
    /// makes it always play the best move it found.
    /// </summary>
    public required int AllowedLoss { get; init; }

    /// <summary>
    /// Chance of picking from the whole tolerated set rather than the top move.
    /// Higher values mean more inconsistent play.
    /// </summary>
    public required double MistakeChance { get; init; }

    public bool UseOpeningBook { get; init; } = true;

    /// <summary>Two-letter badge shown on the opponent card.</summary>
    public required string Initials { get; init; }

    /// <summary>Accent colour for the opponent card, as a CSS hex value.</summary>
    public required string Accent { get; init; }

    /// <summary>The ladder shown in the opponent picker, weakest first.</summary>
    public static IReadOnlyList<BotProfile> All { get; } = [
        new() {
            Name = "Pawn", Elo = 400, Initials = "PA", Accent = "#7d8a97",
            Style = "Just learning. Moves almost at random and misses most threats.",
            Depth = 1, ThinkTimeMilliseconds = 250, AllowedLoss = 900, MistakeChance = 0.85,
            UseOpeningBook = false,
        },
        new() {
            Name = "Rookie", Elo = 800, Initials = "RO", Accent = "#5aa469",
            Style = "Knows the rules and takes free material, but walks into tactics.",
            Depth = 2, ThinkTimeMilliseconds = 400, AllowedLoss = 350, MistakeChance = 0.55,
            UseOpeningBook = false,
        },
        new() {
            Name = "Club", Elo = 1200, Initials = "CL", Accent = "#4a8fd6",
            Style = "Solid basics and simple tactics. Punishes anything you leave hanging.",
            Depth = 4, ThinkTimeMilliseconds = 700, AllowedLoss = 140, MistakeChance = 0.35,
        },
        new() {
            Name = "Expert", Elo = 1600, Initials = "EX", Accent = "#8a6fd1",
            Style = "Plays real openings, calculates several moves ahead and rarely drops material.",
            Depth = 6, ThinkTimeMilliseconds = 1200, AllowedLoss = 55, MistakeChance = 0.20,
        },
        new() {
            Name = "Master", Elo = 2000, Initials = "MA", Accent = "#d69436",
            Style = "Sharp tactics and strong positional judgement. Mistakes will be punished.",
            Depth = 8, ThinkTimeMilliseconds = 2000, AllowedLoss = 20, MistakeChance = 0.08,
        },
        new() {
            Name = "Engine", Elo = 2400, Initials = "EN", Accent = "#d1495b",
            Style = "No handicap. Searches as deep as the time allows and plays the best move it finds.",
            Depth = 64, ThinkTimeMilliseconds = 3000, AllowedLoss = 0, MistakeChance = 0,
        },
    ];

    public static BotProfile Default => All[2];

    public static BotProfile ByName(string name) =>
        All.FirstOrDefault(profile => profile.Name == name) ?? Default;

    /// <summary>
    /// The Elo expected-score curve: the probability this bot beats an opponent of
    /// the given rating, used to report a rating change after a game.
    /// </summary>
    public double ExpectedScoreAgainst(int opponentElo) =>
        1.0 / (1.0 + Math.Pow(10, (opponentElo - Elo) / 400.0));
}
