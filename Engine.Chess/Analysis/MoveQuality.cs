using Engine.Chess.Search;

namespace Engine.Chess.Analysis;

/// <summary>
/// How good a played move was, on the scale players recognise from online chess.
/// </summary>
public enum MoveQuality {
    /// <summary>Played straight from opening theory, so it is not judged at all.</summary>
    Book,

    /// <summary>A sound sacrifice: material is given up and the position is still winning or equal.</summary>
    Brilliant,

    /// <summary>The only move that holds the position; every alternative is markedly worse.</summary>
    Great,

    /// <summary>The move the engine would have played.</summary>
    Best,

    Excellent,
    Good,

    /// <summary>Slightly loose. The position is worse but nothing concrete has been given away.</summary>
    Inaccuracy,

    /// <summary>Real damage: material or a clear share of the advantage.</summary>
    Mistake,

    /// <summary>A forced mate was available and was not played.</summary>
    Miss,

    /// <summary>Loses the game or a decisive amount of material.</summary>
    Blunder,
}

public static class MoveQualities {
    /// <summary>The symbol shown next to the move in the move list.</summary>
    public static string Symbol(this MoveQuality quality) => quality switch {
        MoveQuality.Brilliant => "!!",
        MoveQuality.Great => "!",
        MoveQuality.Best => "☆",
        MoveQuality.Excellent => "✓",
        MoveQuality.Good => "✓",
        MoveQuality.Inaccuracy => "?!",
        MoveQuality.Mistake => "?",
        MoveQuality.Miss => "✗",
        MoveQuality.Blunder => "??",
        _ => "○",
    };

    public static string Label(this MoveQuality quality) => quality switch {
        MoveQuality.Book => "Book",
        MoveQuality.Brilliant => "Brilliant",
        MoveQuality.Great => "Great",
        MoveQuality.Best => "Best",
        MoveQuality.Excellent => "Excellent",
        MoveQuality.Good => "Good",
        MoveQuality.Inaccuracy => "Inaccuracy",
        MoveQuality.Mistake => "Mistake",
        MoveQuality.Miss => "Missed win",
        MoveQuality.Blunder => "Blunder",
        _ => quality.ToString(),
    };

    /// <summary>CSS colour token used by the move list and the review summary.</summary>
    public static string Color(this MoveQuality quality) => quality switch {
        MoveQuality.Brilliant => "var(--quality-brilliant)",
        MoveQuality.Great => "var(--quality-great)",
        MoveQuality.Best => "var(--quality-best)",
        MoveQuality.Excellent => "var(--quality-excellent)",
        MoveQuality.Good => "var(--quality-good)",
        MoveQuality.Inaccuracy => "var(--quality-inaccuracy)",
        MoveQuality.Mistake => "var(--quality-mistake)",
        MoveQuality.Miss => "var(--quality-miss)",
        MoveQuality.Blunder => "var(--quality-blunder)",
        _ => "var(--quality-book)",
    };

    /// <summary>Whether this classification is worth calling out in the game summary.</summary>
    public static bool IsNoteworthy(this MoveQuality quality) => quality is
        MoveQuality.Brilliant or MoveQuality.Great or MoveQuality.Inaccuracy or
        MoveQuality.Mistake or MoveQuality.Miss or MoveQuality.Blunder;
}

/// <summary>
/// Converts centipawn scores into the win-probability scale that accuracy is
/// measured on.
/// </summary>
/// <remarks>
/// Centipawns are the wrong unit for judging a mistake. Going from +0.2 to +0.9 is
/// barely a change in practical terms, whereas going from +0.2 to -0.5 flips who is
/// better; both are a 70-centipawn swing. Mapping to an expected score first means a
/// move is judged by how much it changed the likely result, which is what a player
/// actually feels.
/// </remarks>
public static class WinProbability {
    /// <summary>Fitted against real game outcomes; a two-pawn edge is roughly a 75 percent score.</summary>
    private const double Steepness = 0.00368208;

    /// <summary>Expected score for the side the evaluation favours, as a percentage from 0 to 100.</summary>
    public static double FromCentipawns(int centipawns) {
        if (SearchScores.IsMateScore(centipawns)) return centipawns > 0 ? 100 : 0;
        double clamped = Math.Clamp(centipawns, -2000, 2000);
        return 50 + (50 * ((2.0 / (1.0 + Math.Exp(-Steepness * clamped))) - 1.0));
    }

    /// <summary>
    /// Accuracy for a single move, from the drop in expected score it caused.
    /// A move that loses nothing scores 100; the curve falls away steeply after that.
    /// </summary>
    public static double MoveAccuracy(double winPercentBefore, double winPercentAfter) {
        double lost = Math.Max(0, winPercentBefore - winPercentAfter);
        double accuracy = (103.1668 * Math.Exp(-0.04354 * lost)) - 3.1669;
        return Math.Clamp(accuracy, 0, 100);
    }
}
