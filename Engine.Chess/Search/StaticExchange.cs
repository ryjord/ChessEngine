using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Evaluation;

namespace Engine.Chess.Search;

/// <summary>
/// Static exchange evaluation: the material a capture wins or loses once both
/// sides have traded off on the square as favourably as they can.
/// </summary>
/// <remarks>
/// This is what separates "the queen can take that defended pawn" from "the queen
/// should take that defended pawn". It matters most for move ordering, where
/// putting losing captures last saves a large number of nodes.
/// </remarks>
public static class StaticExchange {
    /// <summary>Values used for the swap-off; deliberately simple and colour-independent.</summary>
    private static readonly int[] Value = [0, 100, 320, 330, 500, 900, 10000];

    /// <summary>True when the capture does not lose more than <paramref name="threshold"/> centipawns.</summary>
    public static bool IsGoodCapture(Position position, Move move, int threshold = 0) =>
        Evaluate(position, move) >= threshold;

    public static int Evaluate(Position position, Move move) {
        int to = move.To;
        int from = move.From;

        // Promotions and en passant change material in ways the plain swap loop does
        // not model, so their gain is folded in before the exchange starts.
        int gain = move.IsEnPassant
            ? Value[(int)PieceType.Pawn]
            : Value[(int)position.PieceAt(to).TypeOf()];

        if (move.IsPromotion) gain += Value[(int)move.PromotionPiece] - Value[(int)PieceType.Pawn];

        PieceType attacker = move.IsPromotion ? move.PromotionPiece : position.PieceAt(from).TypeOf();

        ulong occupied = position.Occupied ^ Bitboards.Square(from);
        if (move.IsEnPassant) {
            occupied ^= Bitboards.Square(position.SideToMove == Color.White ? to - 8 : to + 8);
        }
        occupied |= Bitboards.Square(to);

        ulong attackers = position.AttackersTo(to, occupied) & occupied;
        Color side = position.SideToMove.Opponent();

        // gain[d] is the material balance if the exchange were to stop after d recaptures.
        Span<int> gains = stackalloc int[32];
        gains[0] = gain;
        int depth = 0;

        while (depth < gains.Length - 1) {
            ulong sideAttackers = attackers & position.Bitboard(side) & occupied;
            if (sideAttackers == 0) break;

            PieceType cheapest = LeastValuableAttacker(position, sideAttackers, side, out int attackerSquare);
            if (cheapest == PieceType.None) break;

            depth++;
            gains[depth] = Value[(int)attacker] - gains[depth - 1];

            // If both continuing and stopping already lose material, the rest of the
            // sequence cannot change the verdict.
            if (Math.Max(-gains[depth - 1], gains[depth]) < 0) break;

            attacker = cheapest;
            occupied ^= Bitboards.Square(attackerSquare);
            // Removing a piece can uncover a slider that was hidden behind it.
            attackers = position.AttackersTo(to, occupied) & occupied;
            side = side.Opponent();
        }

        // Fold the sequence back, because at each ply the side to move may simply
        // decline to recapture rather than accept a losing trade.
        while (depth > 0) {
            gains[depth - 1] = -Math.Max(-gains[depth - 1], gains[depth]);
            depth--;
        }
        return gains[0];
    }

    private static PieceType LeastValuableAttacker(
        Position position, ulong attackers, Color side, out int square) {
        for (PieceType type = PieceType.Pawn; type <= PieceType.King; type++) {
            ulong candidates = attackers & position.Bitboard(side, type);
            if (candidates == 0) continue;
            square = Bitboards.LeastSignificant(candidates);
            return type;
        }
        square = Squares.None;
        return PieceType.None;
    }

    /// <summary>Most Valuable Victim / Least Valuable Aggressor: the cheap first pass at ordering captures.</summary>
    public static int MvvLva(Position position, Move move) {
        int victim = move.IsEnPassant
            ? Evaluator.PieceValue[(int)PieceType.Pawn]
            : Evaluator.PieceValue[(int)position.PieceAt(move.To).TypeOf()];
        int aggressor = Evaluator.PieceValue[(int)position.PieceAt(move.From).TypeOf()];
        return (victim * 16) - aggressor;
    }
}
