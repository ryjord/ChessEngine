namespace Engine.Chess.Search;

/// <summary>When the search should stop. The first limit reached wins.</summary>
public sealed class SearchLimits {
    /// <summary>Deepest iteration to attempt.</summary>
    public int MaxDepth { get; init; } = 64;

    /// <summary>Wall-clock budget in milliseconds. Zero or less means no time limit.</summary>
    public int MaxTimeMilliseconds { get; init; }

    /// <summary>Node ceiling, which keeps a search reproducible regardless of machine speed.</summary>
    public long MaxNodes { get; init; } = long.MaxValue;

    /// <summary>Stops the search early once a forced mate has been proven.</summary>
    public bool StopOnMate { get; init; } = true;

    public static SearchLimits ToDepth(int depth) => new() { MaxDepth = depth };

    public static SearchLimits ToTime(int milliseconds, int maxDepth = 64) =>
        new() { MaxTimeMilliseconds = milliseconds, MaxDepth = maxDepth };
}
