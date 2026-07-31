using Engine.Chess.Core;

namespace Engine.Chess.Search;

/// <summary>The outcome of one completed search, including the line it expects to be played.</summary>
public sealed class SearchResult {
    public required Move BestMove { get; init; }

    /// <summary>Score in centipawns from the searching side's point of view.</summary>
    public required int Score { get; init; }

    public required int Depth { get; init; }

    public required long Nodes { get; init; }

    public required int ElapsedMilliseconds { get; init; }

    /// <summary>The principal variation: the line both sides are expected to play.</summary>
    public required IReadOnlyList<Move> PrincipalVariation { get; init; }

    /// <summary>Moves to mate if the score is a forced mate, negative when the searching side is mated.</summary>
    public int? MateIn => SearchScores.ToMateDistance(Score);

    public bool IsMate => MateIn.HasValue;

    public long NodesPerSecond =>
        ElapsedMilliseconds > 0 ? Nodes * 1000 / ElapsedMilliseconds : Nodes;

    public static SearchResult Empty(Move move = default) => new() {
        BestMove = move,
        Score = 0,
        Depth = 0,
        Nodes = 0,
        ElapsedMilliseconds = 0,
        PrincipalVariation = [],
    };
}

/// <summary>
/// A snapshot of a search in flight, reported after each completed depth so a host
/// can show what the engine is doing rather than an indeterminate spinner.
/// </summary>
public sealed record SearchProgress {
    public required int Depth { get; init; }

    public required long Nodes { get; init; }

    public required int ElapsedMilliseconds { get; init; }

    /// <summary>Score from the searching side's point of view, or null while still ordering moves.</summary>
    public int? Score { get; init; }

    public IReadOnlyList<Move> PrincipalVariation { get; init; } = [];

    public long NodesPerSecond => ElapsedMilliseconds > 0 ? Nodes * 1000 / ElapsedMilliseconds : 0;
}

/// <summary>Score conventions shared by the search, the transposition table and the UI.</summary>
public static class SearchScores {
    /// <summary>Score of being mated right now. Kept clear of <c>short</c> limits so it survives the table.</summary>
    public const int Mate = 30000;

    /// <summary>Any score at least this large is a forced mate rather than a positional judgement.</summary>
    public const int MateThreshold = Mate - 1000;

    public const int Infinity = 32000;

    public const int Draw = 0;

    public static bool IsMateScore(int score) => Math.Abs(score) >= MateThreshold;

    /// <summary>
    /// Converts a mate score into a move count: positive when the side to move mates,
    /// negative when it is mated, null when the score is not a mate.
    /// </summary>
    public static int? ToMateDistance(int score) {
        if (!IsMateScore(score)) return null;
        int plies = Mate - Math.Abs(score);
        int moves = (plies + 1) / 2;
        return score > 0 ? moves : -moves;
    }
}
