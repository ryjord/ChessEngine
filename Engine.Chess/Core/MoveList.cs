using System.Runtime.CompilerServices;

namespace Engine.Chess.Core;

/// <summary>
/// A fixed-capacity move buffer stored inline in the struct, so generating moves
/// during search costs no heap allocation. Always pass it by <c>ref</c>.
/// </summary>
public struct MoveList {
    /// <summary>Comfortably above the ~218 move ceiling of any reachable chess position.</summary>
    public const int Capacity = 256;

    private Buffer _moves;

    public int Count { get; private set; }

    public Move this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _moves[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _moves[index] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Move move) => _moves[Count++] = move;

    public void Clear() => Count = 0;

    public void Swap(int first, int second) => (_moves[first], _moves[second]) = (_moves[second], _moves[first]);

    public bool Contains(Move move) {
        for (int i = 0; i < Count; i++) {
            if (_moves[i] == move) return true;
        }
        return false;
    }

    public Move[] ToArray() {
        var moves = new Move[Count];
        for (int i = 0; i < Count; i++) moves[i] = _moves[i];
        return moves;
    }

    [InlineArray(Capacity)]
    private struct Buffer {
        private Move _element;
    }
}
