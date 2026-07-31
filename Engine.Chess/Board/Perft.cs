using Engine.Chess.Core;

namespace Engine.Chess.Board;

/// <summary>
/// Move-path enumeration. Counting the leaf nodes at a fixed depth and comparing
/// against published totals is the only practical way to prove a move generator
/// handles every rule interaction, so this is a first-class part of the engine
/// rather than test-only scaffolding.
/// </summary>
public static class Perft {
    /// <summary>Counts legal move sequences of exactly <paramref name="depth"/> plies.</summary>
    public static long Run(Position position, int depth) {
        if (depth <= 0) return 1;

        MoveList moves = default;
        MoveGenerator.Generate(position, ref moves);
        if (depth == 1) return moves.Count;

        long nodes = 0;
        for (int i = 0; i < moves.Count; i++) {
            position.MakeMove(moves[i]);
            nodes += Run(position, depth - 1);
            position.UnmakeMove();
        }
        return nodes;
    }

    /// <summary>
    /// Per-root-move leaf counts, which is what you diff against a reference engine
    /// to find exactly which move subtree is wrong.
    /// </summary>
    public static IReadOnlyDictionary<string, long> Divide(Position position, int depth) {
        var breakdown = new Dictionary<string, long>();
        MoveList moves = default;
        MoveGenerator.Generate(position, ref moves);

        for (int i = 0; i < moves.Count; i++) {
            position.MakeMove(moves[i]);
            breakdown[moves[i].ToUci()] = Run(position, depth - 1);
            position.UnmakeMove();
        }
        return breakdown;
    }
}
