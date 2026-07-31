using Engine.Chess.Core;

namespace Engine.Chess.Evaluation;

/// <summary>
/// Positional bonuses for a piece standing on a square, from white's point of view.
/// Each piece has a middlegame and an endgame table because good squares change as
/// material comes off: a king that wants the corner on move 20 wants the centre on
/// move 60.
/// </summary>
/// <remarks>
/// The literals below are written the way a board is drawn, with the eighth rank on
/// the first line, then flipped once at startup into the a1-indexed layout the rest
/// of the engine uses. Reading them should not require mentally inverting the board.
/// </remarks>
public static class PieceSquareTables {
    /// <summary>Indexed by <see cref="PieceType"/>, then by square. Middlegame weights.</summary>
    public static readonly int[][] Midgame = new int[7][];

    /// <summary>Indexed by <see cref="PieceType"/>, then by square. Endgame weights.</summary>
    public static readonly int[][] Endgame = new int[7][];

    static PieceSquareTables() {
        Midgame[(int)PieceType.Pawn] = ToBoardOrder(PawnMidgame);
        Midgame[(int)PieceType.Knight] = ToBoardOrder(KnightMidgame);
        Midgame[(int)PieceType.Bishop] = ToBoardOrder(BishopMidgame);
        Midgame[(int)PieceType.Rook] = ToBoardOrder(RookMidgame);
        Midgame[(int)PieceType.Queen] = ToBoardOrder(QueenMidgame);
        Midgame[(int)PieceType.King] = ToBoardOrder(KingMidgame);

        Endgame[(int)PieceType.Pawn] = ToBoardOrder(PawnEndgame);
        Endgame[(int)PieceType.Knight] = ToBoardOrder(KnightEndgame);
        Endgame[(int)PieceType.Bishop] = ToBoardOrder(BishopEndgame);
        Endgame[(int)PieceType.Rook] = ToBoardOrder(RookEndgame);
        Endgame[(int)PieceType.Queen] = ToBoardOrder(QueenEndgame);
        Endgame[(int)PieceType.King] = ToBoardOrder(KingEndgame);

        Midgame[(int)PieceType.None] = new int[Squares.Count];
        Endgame[(int)PieceType.None] = new int[Squares.Count];
    }

    /// <summary>Turns a visually-written table (a8 first) into one indexed by square (a1 first).</summary>
    private static int[] ToBoardOrder(ReadOnlySpan<int> visual) {
        var table = new int[Squares.Count];
        for (int square = 0; square < Squares.Count; square++) table[square] = visual[Squares.Flip(square)];
        return table;
    }

    // Pawns are pushed towards the centre early and towards promotion late.
    private static ReadOnlySpan<int> PawnMidgame =>
    [
          0,   0,   0,   0,   0,   0,   0,   0,
         50,  50,  50,  50,  50,  50,  50,  50,
         12,  18,  25,  32,  32,  25,  18,  12,
          4,   8,  14,  26,  26,  14,   8,   4,
          0,   2,   6,  22,  22,   6,   2,   0,
          4,  -4,  -6,   4,   4,  -8,  -4,   4,
          6,  10,  10, -18, -18,  10,  10,   6,
          0,   0,   0,   0,   0,   0,   0,   0,
    ];

    private static ReadOnlySpan<int> PawnEndgame =>
    [
          0,   0,   0,   0,   0,   0,   0,   0,
         92,  92,  92,  92,  92,  92,  92,  92,
         56,  56,  50,  46,  46,  50,  56,  56,
         32,  30,  26,  22,  22,  26,  30,  32,
         14,  12,  10,   8,   8,  10,  12,  14,
          6,   6,   4,   4,   4,   4,   6,   6,
          2,   2,   2,   2,   2,   2,   2,   2,
          0,   0,   0,   0,   0,   0,   0,   0,
    ];

    // Knights are worth far more near the centre; a rim knight is dim.
    private static ReadOnlySpan<int> KnightMidgame =>
    [
        -50, -40, -30, -30, -30, -30, -40, -50,
        -40, -20,   0,   5,   5,   0, -20, -40,
        -30,   5,  15,  20,  20,  15,   5, -30,
        -30,   0,  20,  25,  25,  20,   0, -30,
        -30,   5,  20,  25,  25,  20,   5, -30,
        -30,   0,  15,  20,  20,  15,   0, -30,
        -40, -20,   0,   5,   5,   0, -20, -40,
        -50, -40, -30, -30, -30, -30, -40, -50,
    ];

    private static ReadOnlySpan<int> KnightEndgame =>
    [
        -42, -32, -24, -24, -24, -24, -32, -42,
        -32, -16,   0,   4,   4,   0, -16, -32,
        -24,   4,  12,  16,  16,  12,   4, -24,
        -24,   0,  16,  20,  20,  16,   0, -24,
        -24,   4,  16,  20,  20,  16,   4, -24,
        -24,   0,  12,  16,  16,  12,   0, -24,
        -32, -16,   0,   4,   4,   0, -16, -32,
        -42, -32, -24, -24, -24, -24, -32, -42,
    ];

    private static ReadOnlySpan<int> BishopMidgame =>
    [
        -20, -10, -10, -10, -10, -10, -10, -20,
        -10,   5,   0,   0,   0,   0,   5, -10,
        -10,  10,  10,  10,  10,  10,  10, -10,
        -10,   0,  10,  15,  15,  10,   0, -10,
        -10,   5,   5,  15,  15,   5,   5, -10,
        -10,   0,   5,  10,  10,   5,   0, -10,
        -10,   5,   0,   0,   0,   0,   5, -10,
        -20, -10, -10, -10, -10, -10, -10, -20,
    ];

    private static ReadOnlySpan<int> BishopEndgame =>
    [
        -14,  -8,  -6,  -6,  -6,  -6,  -8, -14,
         -8,   2,   0,   0,   0,   0,   2,  -8,
         -6,   6,   8,   8,   8,   8,   6,  -6,
         -6,   0,   8,  12,  12,   8,   0,  -6,
         -6,   2,   4,  12,  12,   4,   2,  -6,
         -6,   0,   4,   8,   8,   4,   0,  -6,
         -8,   0,   0,   0,   0,   0,   0,  -8,
        -14,  -8,  -6,  -6,  -6,  -6,  -8, -14,
    ];

    // Rooks belong on the seventh rank and on central files.
    private static ReadOnlySpan<int> RookMidgame =>
    [
          0,   0,   0,   0,   0,   0,   0,   0,
          8,  12,  12,  12,  12,  12,  12,   8,
         -4,   0,   0,   0,   0,   0,   0,  -4,
         -4,   0,   0,   0,   0,   0,   0,  -4,
         -4,   0,   0,   0,   0,   0,   0,  -4,
         -4,   0,   0,   0,   0,   0,   0,  -4,
         -4,   0,   0,   0,   0,   0,   0,  -4,
         -2,   0,   4,   8,   8,   4,   0,  -2,
    ];

    private static ReadOnlySpan<int> RookEndgame =>
    [
          6,   6,   6,   6,   6,   6,   6,   6,
         10,  10,  10,  10,  10,  10,  10,  10,
          2,   2,   2,   2,   2,   2,   2,   2,
          0,   0,   0,   0,   0,   0,   0,   0,
          0,   0,   0,   0,   0,   0,   0,   0,
         -2,  -2,  -2,  -2,  -2,  -2,  -2,  -2,
         -4,  -4,  -4,  -4,  -4,  -4,  -4,  -4,
         -4,  -2,   0,   0,   0,   0,  -2,  -4,
    ];

    private static ReadOnlySpan<int> QueenMidgame =>
    [
        -20, -10, -10,  -4,  -4, -10, -10, -20,
        -10,   0,   0,   0,   0,   0,   0, -10,
        -10,   0,   4,   4,   4,   4,   0, -10,
         -4,   0,   4,   4,   4,   4,   0,  -4,
          0,   0,   4,   4,   4,   4,   0,  -4,
        -10,   4,   4,   4,   4,   4,   0, -10,
        -10,   0,   4,   0,   0,   0,   0, -10,
        -20, -10, -10,  -4,  -4, -10, -10, -20,
    ];

    private static ReadOnlySpan<int> QueenEndgame =>
    [
        -18, -10,  -6,  -2,  -2,  -6, -10, -18,
        -10,   4,   8,  10,  10,   8,   4, -10,
         -6,   8,  14,  16,  16,  14,   8,  -6,
         -2,  10,  16,  20,  20,  16,  10,  -2,
         -2,  10,  16,  20,  20,  16,  10,  -2,
         -6,   8,  14,  16,  16,  14,   8,  -6,
        -10,   4,   8,  10,  10,   8,   4, -10,
        -18, -10,  -6,  -2,  -2,  -6, -10, -18,
    ];

    // In the middlegame the king hides behind its pawns on the wing it castled to.
    private static ReadOnlySpan<int> KingMidgame =>
    [
        -60, -70, -70, -80, -80, -70, -70, -60,
        -60, -70, -70, -80, -80, -70, -70, -60,
        -60, -70, -70, -80, -80, -70, -70, -60,
        -60, -70, -70, -80, -80, -70, -70, -60,
        -40, -50, -50, -60, -60, -50, -50, -40,
        -20, -30, -30, -40, -40, -30, -30, -20,
         20,  20, -10, -20, -20, -10,  20,  20,
         20,  34,  10,   0,   0,  10,  36,  20,
    ];

    // In the endgame it becomes a fighting piece and wants the middle of the board.
    private static ReadOnlySpan<int> KingEndgame =>
    [
        -50, -30, -30, -30, -30, -30, -30, -50,
        -30, -20, -10,   0,   0, -10, -20, -30,
        -30, -10,  20,  30,  30,  20, -10, -30,
        -30, -10,  30,  40,  40,  30, -10, -30,
        -30, -10,  30,  40,  40,  30, -10, -30,
        -30, -10,  20,  30,  30,  20, -10, -30,
        -30, -30,   0,   0,   0,   0, -30, -30,
        -50, -40, -30, -20, -20, -30, -40, -50,
    ];
}
