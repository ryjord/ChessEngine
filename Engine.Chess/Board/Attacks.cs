using System.Numerics;
using System.Runtime.CompilerServices;
using Engine.Chess.Core;

namespace Engine.Chess.Board;

/// <summary>
/// Precomputed attack sets. Leaping pieces are looked up directly; sliding pieces
/// use the classical ray method, where a ray is truncated at its first blocker by
/// clearing the blocker's own ray in the same direction.
/// </summary>
public static class Attacks {
    private const int North = 0, NorthEast = 1, East = 2, SouthEast = 3;
    private const int South = 4, SouthWest = 5, West = 6, NorthWest = 7;

    private static readonly ulong[,] Rays = new ulong[8, Squares.Count];
    private static readonly ulong[] KnightTable = new ulong[Squares.Count];
    private static readonly ulong[] KingTable = new ulong[Squares.Count];
    private static readonly ulong[,] PawnTable = new ulong[2, Squares.Count];

    /// <summary>Squares strictly between two squares on a shared rank, file or diagonal.</summary>
    private static readonly ulong[,] BetweenTable = new ulong[Squares.Count, Squares.Count];

    /// <summary>The full line through two aligned squares, or zero when they are not aligned.</summary>
    private static readonly ulong[,] LineTable = new ulong[Squares.Count, Squares.Count];

    static Attacks() {
        BuildRays();
        BuildLeapers();
        BuildLines();
    }

    /// <summary>Forces the static tables to be built now rather than on first use.</summary>
    public static void Initialize() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Knight(int square) => KnightTable[square];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong King(int square) => KingTable[square];

    /// <summary>The two squares a pawn of <paramref name="color"/> on <paramref name="square"/> attacks.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Pawn(int square, Color color) => PawnTable[(int)color, square];

    public static ulong Rook(int square, ulong occupied) =>
        RayAttack(North, square, occupied) | RayAttack(East, square, occupied) |
        RayAttack(South, square, occupied) | RayAttack(West, square, occupied);

    public static ulong Bishop(int square, ulong occupied) =>
        RayAttack(NorthEast, square, occupied) | RayAttack(SouthEast, square, occupied) |
        RayAttack(SouthWest, square, occupied) | RayAttack(NorthWest, square, occupied);

    public static ulong Queen(int square, ulong occupied) => Rook(square, occupied) | Bishop(square, occupied);

    public static ulong Of(PieceType type, int square, ulong occupied) => type switch {
        PieceType.Knight => KnightTable[square],
        PieceType.Bishop => Bishop(square, occupied),
        PieceType.Rook => Rook(square, occupied),
        PieceType.Queen => Queen(square, occupied),
        PieceType.King => KingTable[square],
        _ => 0UL,
    };

    /// <summary>Squares strictly between <paramref name="from"/> and <paramref name="to"/>, empty if unaligned.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Between(int from, int to) => BetweenTable[from, to];

    /// <summary>
    /// Every square on the line through two aligned squares, including both ends.
    /// Zero when they share no rank, file or diagonal. Used to constrain pinned pieces.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Line(int from, int to) => LineTable[from, to];

    /// <summary>
    /// Walks a ray until it meets an occupied square, which stays included so that
    /// captures are generated and defended pieces are seen as attacked.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RayAttack(int direction, int square, ulong occupied) {
        ulong ray = Rays[direction, square];
        ulong blockers = ray & occupied;
        if (blockers == 0) return ray;

        // Positive directions advance towards h8, so their nearest blocker is the
        // lowest set bit; negative directions take the highest.
        int blocker = direction <= East || direction == NorthWest
            ? BitOperations.TrailingZeroCount(blockers)
            : 63 - BitOperations.LeadingZeroCount(blockers);
        return ray & ~Rays[direction, blocker];
    }

    private static void BuildRays() {
        ReadOnlySpan<int> fileStep = [0, 1, 1, 1, 0, -1, -1, -1];
        ReadOnlySpan<int> rankStep = [1, 1, 0, -1, -1, -1, 0, 1];

        for (int square = 0; square < Squares.Count; square++) {
            for (int direction = 0; direction < 8; direction++) {
                int file = Squares.FileOf(square);
                int rank = Squares.RankOf(square);
                ulong ray = 0UL;
                while (true) {
                    file += fileStep[direction];
                    rank += rankStep[direction];
                    if ((uint)file > 7 || (uint)rank > 7) break;
                    ray |= Bitboards.Square(Squares.From(file, rank));
                }
                Rays[direction, square] = ray;
            }
        }
    }

    private static void BuildLeapers() {
        for (int square = 0; square < Squares.Count; square++) {
            ulong board = Bitboards.Square(square);

            KnightTable[square] =
                ((board << 17) & Bitboards.NotFileA) | ((board << 15) & Bitboards.NotFileH) |
                ((board << 10) & ~(Bitboards.FileA | Bitboards.FileB)) |
                ((board << 6) & ~(Bitboards.FileG | Bitboards.FileH)) |
                ((board >> 17) & Bitboards.NotFileH) | ((board >> 15) & Bitboards.NotFileA) |
                ((board >> 10) & ~(Bitboards.FileG | Bitboards.FileH)) |
                ((board >> 6) & ~(Bitboards.FileA | Bitboards.FileB));

            KingTable[square] =
                Bitboards.North(board) | Bitboards.South(board) |
                ((board << 1) & Bitboards.NotFileA) | ((board >> 1) & Bitboards.NotFileH) |
                Bitboards.NorthEast(board) | Bitboards.NorthWest(board) |
                Bitboards.SouthEast(board) | Bitboards.SouthWest(board);

            PawnTable[(int)Color.White, square] = Bitboards.PawnAttacks(board, Color.White);
            PawnTable[(int)Color.Black, square] = Bitboards.PawnAttacks(board, Color.Black);
        }
    }

    private static void BuildLines() {
        for (int from = 0; from < Squares.Count; from++) {
            for (int direction = 0; direction < 8; direction++) {
                ulong ray = Rays[direction, from];
                ulong walked = 0UL;
                ulong remaining = ray;
                while (remaining != 0) {
                    // Ray bits are visited in ascending square order, which is only
                    // outward-from-`from` for the four positive directions.
                    int to = direction <= East || direction == NorthWest
                        ? Bitboards.LeastSignificant(remaining)
                        : 63 - BitOperations.LeadingZeroCount(remaining);
                    remaining &= ~Bitboards.Square(to);

                    BetweenTable[from, to] = walked;
                    BetweenTable[to, from] = walked;
                    walked |= Bitboards.Square(to);

                    ulong line = Rays[direction, from] | Rays[(direction + 4) % 8, from]
                               | Bitboards.Square(from) | Bitboards.Square(to);
                    LineTable[from, to] = line;
                    LineTable[to, from] = line;
                }
            }
        }
    }
}
