using System.Numerics;
using System.Runtime.CompilerServices;

namespace Engine.Chess.Core;

/// <summary>
/// Helpers for the 64-bit board sets. Bit <c>n</c> corresponds to square <c>n</c>
/// under the little-endian rank-file mapping described in <see cref="Squares"/>.
/// </summary>
public static class Bitboards {
    public const ulong Empty = 0UL;
    public const ulong Full = ulong.MaxValue;

    public const ulong FileA = 0x0101010101010101UL;
    public const ulong FileB = FileA << 1;
    public const ulong FileG = FileA << 6;
    public const ulong FileH = FileA << 7;

    public const ulong Rank1 = 0x00000000000000FFUL;
    public const ulong Rank2 = Rank1 << 8;
    public const ulong Rank3 = Rank1 << 16;
    public const ulong Rank4 = Rank1 << 24;
    public const ulong Rank5 = Rank1 << 32;
    public const ulong Rank6 = Rank1 << 40;
    public const ulong Rank7 = Rank1 << 48;
    public const ulong Rank8 = Rank1 << 56;

    public const ulong NotFileA = ~FileA;
    public const ulong NotFileH = ~FileH;

    public const ulong LightSquares = 0x55AA55AA55AA55AAUL;
    public const ulong DarkSquares = ~LightSquares;

    /// <summary>Per-file masks, indexed 0 (a-file) to 7 (h-file).</summary>
    public static readonly ulong[] Files = BuildFiles();

    /// <summary>Per-rank masks, indexed 0 (rank 1) to 7 (rank 8).</summary>
    public static readonly ulong[] Ranks = BuildRanks();

    /// <summary>The file of a square plus its neighbouring files; used for isolated-pawn tests.</summary>
    public static readonly ulong[] AdjacentFiles = BuildAdjacentFiles();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Square(int square) => 1UL << square;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains(ulong board, int square) => (board & (1UL << square)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopCount(ulong board) => BitOperations.PopCount(board);

    /// <summary>Index of the least significant set bit. The board must not be empty.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LeastSignificant(ulong board) => BitOperations.TrailingZeroCount(board);

    /// <summary>Removes and returns the least significant set bit. The board must not be empty.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopLeastSignificant(ref ulong board) {
        int square = BitOperations.TrailingZeroCount(board);
        board &= board - 1;
        return square;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong North(ulong board) => board << 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong South(ulong board) => board >> 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong NorthEast(ulong board) => (board << 9) & NotFileA;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong NorthWest(ulong board) => (board << 7) & NotFileH;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SouthEast(ulong board) => (board >> 7) & NotFileA;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SouthWest(ulong board) => (board >> 9) & NotFileH;

    /// <summary>Both forward-diagonal attacks for every pawn in <paramref name="pawns"/> at once.</summary>
    public static ulong PawnAttacks(ulong pawns, Color color) =>
        color == Color.White ? NorthEast(pawns) | NorthWest(pawns)
                             : SouthEast(pawns) | SouthWest(pawns);

    /// <summary>Renders a bitboard as eight lines of dots and hashes, a8 first. Debug aid.</summary>
    public static string ToDisplayString(ulong board) {
        var text = new System.Text.StringBuilder(80);
        for (int rank = 7; rank >= 0; rank--) {
            for (int file = 0; file < 8; file++) {
                text.Append(Contains(board, Squares.From(file, rank)) ? '#' : '.');
            }
            text.Append('\n');
        }
        return text.ToString();
    }

    private static ulong[] BuildFiles() {
        var files = new ulong[8];
        for (int file = 0; file < 8; file++) files[file] = FileA << file;
        return files;
    }

    private static ulong[] BuildRanks() {
        var ranks = new ulong[8];
        for (int rank = 0; rank < 8; rank++) ranks[rank] = Rank1 << (rank * 8);
        return ranks;
    }

    private static ulong[] BuildAdjacentFiles() {
        var files = BuildFiles();
        var adjacent = new ulong[8];
        for (int file = 0; file < 8; file++) {
            adjacent[file] = files[file];
            if (file > 0) adjacent[file] |= files[file - 1];
            if (file < 7) adjacent[file] |= files[file + 1];
        }
        return adjacent;
    }
}
