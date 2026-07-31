namespace Engine.Chess.Core;

/// <summary>
/// Square helpers for the little-endian rank-file mapping used throughout the engine:
/// index 0 is a1, index 7 is h1, index 56 is a8 and index 63 is h8.
/// </summary>
public static class Squares {
    public const int Count = 64;
    public const int None = -1;

    public const int A1 = 0, B1 = 1, C1 = 2, D1 = 3, E1 = 4, F1 = 5, G1 = 6, H1 = 7;
    public const int A8 = 56, B8 = 57, C8 = 58, D8 = 59, E8 = 60, F8 = 61, G8 = 62, H8 = 63;

    public static int RankOf(int square) => square >> 3;

    public static int FileOf(int square) => square & 7;

    public static int From(int file, int rank) => (rank << 3) + file;

    /// <summary>Mirrors a square vertically, mapping a1 to a8. Used to read white tables for black.</summary>
    public static int Flip(int square) => square ^ 56;

    public static bool IsValid(int square) => (uint)square < Count;

    /// <summary>Chebyshev distance, used by endgame king-proximity terms.</summary>
    public static int Distance(int a, int b) =>
        Math.Max(Math.Abs(FileOf(a) - FileOf(b)), Math.Abs(RankOf(a) - RankOf(b)));

    public static string ToName(int square) =>
        IsValid(square) ? $"{(char)('a' + FileOf(square))}{RankOf(square) + 1}" : "-";

    public static int FromName(ReadOnlySpan<char> name) {
        if (name.Length != 2) return None;
        int file = name[0] - 'a';
        int rank = name[1] - '1';
        if ((uint)file > 7 || (uint)rank > 7) return None;
        return From(file, rank);
    }
}
