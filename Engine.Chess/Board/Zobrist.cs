using Engine.Chess.Core;

namespace Engine.Chess.Board;

/// <summary>
/// Zobrist keys for incremental position hashing. Keys come from a fixed-seed
/// generator so that hashes are reproducible across runs, which keeps opening
/// books and regression tests stable.
/// </summary>
public static class Zobrist {
    private const ulong Seed = 0x9E3779B97F4A7C15UL;

    public static readonly ulong[,] PieceSquare = new ulong[Pieces.Count, Squares.Count];
    public static readonly ulong[] Castling = new ulong[16];
    public static readonly ulong[] EnPassantFile = new ulong[8];
    public static readonly ulong SideToMove;

    static Zobrist() {
        ulong state = Seed;

        for (int piece = 0; piece < Pieces.Count; piece++) {
            for (int square = 0; square < Squares.Count; square++) {
                PieceSquare[piece, square] = Next(ref state);
            }
        }
        for (int rights = 0; rights < Castling.Length; rights++) Castling[rights] = Next(ref state);
        for (int file = 0; file < EnPassantFile.Length; file++) EnPassantFile[file] = Next(ref state);
        SideToMove = Next(ref state);
    }

    /// <summary>splitmix64, chosen for good bit dispersion from a trivial amount of state.</summary>
    private static ulong Next(ref ulong state) {
        state += 0x9E3779B97F4A7C15UL;
        ulong value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
