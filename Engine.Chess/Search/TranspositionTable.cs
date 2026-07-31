using Engine.Chess.Core;

namespace Engine.Chess.Search;

/// <summary>What a stored score tells us about the true value of a position.</summary>
public enum ScoreBound : byte {
    /// <summary>The score is exact: the position was searched with a full window.</summary>
    Exact = 0,

    /// <summary>The true score is at least this high; the search cut off on a beta cutoff.</summary>
    Lower = 1,

    /// <summary>The true score is no higher than this; no move beat alpha.</summary>
    Upper = 2,
}

/// <summary>
/// A hash table of previously searched positions. Transpositions are extremely
/// common, so reusing a result rather than re-searching a subtree is worth more
/// than any single pruning heuristic.
/// </summary>
/// <remarks>
/// Entries are keyed by the upper half of the Zobrist hash. Collisions are possible
/// but vanishingly rare, and a stored move is always re-validated by the search
/// before it is played, so a collision costs time rather than correctness.
/// </remarks>
public sealed class TranspositionTable {
    private Entry[] _entries = [];
    private int _indexMask;
    private byte _generation;

    /// <param name="sizeInMegabytes">Rounded down to the nearest power-of-two entry count.</param>
    public TranspositionTable(int sizeInMegabytes = 16) {
        Resize(sizeInMegabytes);
    }

    public int Capacity => _entries.Length;

    public void Resize(int sizeInMegabytes) {
        int bytesPerEntry = System.Runtime.InteropServices.Marshal.SizeOf<Entry>();
        long requested = Math.Max(1, (long)sizeInMegabytes) * 1024 * 1024 / bytesPerEntry;

        int count = 1;
        while (count * 2L <= requested) count *= 2;

        _entries = new Entry[count];
        _indexMask = count - 1;
        _generation = 0;
    }

    public void Clear() {
        Array.Clear(_entries);
        _generation = 0;
    }

    /// <summary>Marks the start of a new search so older entries can be replaced first.</summary>
    public void NewSearch() => _generation++;

    /// <summary>
    /// Looks up a position. <paramref name="score"/> is only meaningful when this
    /// returns true; <paramref name="move"/> is filled in whenever one was stored,
    /// because a move from a too-shallow entry is still worth trying first.
    /// </summary>
    public bool Probe(ulong key, int depth, int ply, int alpha, int beta, out int score, out Move move) {
        ref Entry entry = ref _entries[key & (ulong)_indexMask];
        score = 0;
        move = Move.None;

        if (entry.Key != Verification(key)) return false;

        move = Move.FromEncoded(entry.Move);
        if (entry.Depth < depth) return false;

        int stored = FromTableScore(entry.Score, ply);
        switch (entry.Bound) {
            case ScoreBound.Exact:
                score = stored;
                return true;
            case ScoreBound.Lower when stored >= beta:
                score = stored;
                return true;
            case ScoreBound.Upper when stored <= alpha:
                score = stored;
                return true;
            default:
                return false;
        }
    }

    public void Store(ulong key, int depth, int ply, int score, ScoreBound bound, Move move) {
        ref Entry entry = ref _entries[key & (ulong)_indexMask];
        uint verification = Verification(key);

        // Keep the deeper result for the same position, but always let a new search
        // overwrite a stale one so the table does not fill up with dead entries.
        bool sameSlot = entry.Key == verification;
        if (sameSlot && entry.Depth > depth && entry.Generation == _generation) return;

        // Never drop a usable move in favour of an empty one.
        ushort encoded = move.IsNull && sameSlot ? entry.Move : move.Encoded;

        entry = new Entry {
            Key = verification,
            Move = encoded,
            Score = (short)ToTableScore(score, ply),
            Depth = (short)depth,
            Bound = bound,
            Generation = _generation,
        };
    }

    /// <summary>Reads back the best move for a position regardless of depth, for walking the PV.</summary>
    public Move ProbeMove(ulong key) {
        ref Entry entry = ref _entries[key & (ulong)_indexMask];
        return entry.Key == Verification(key) ? Move.FromEncoded(entry.Move) : Move.None;
    }

    /// <summary>Approximate fill rate in permille, sampled from the first 1000 slots.</summary>
    public int HashFull() {
        int used = 0;
        int sample = Math.Min(1000, _entries.Length);
        for (int i = 0; i < sample; i++) {
            if (_entries[i].Key != 0 && _entries[i].Generation == _generation) used++;
        }
        return sample == 0 ? 0 : used * 1000 / sample;
    }

    private static uint Verification(ulong key) => (uint)(key >> 32);

    /// <summary>
    /// Mate scores count plies from the root, but a table entry may be reached at a
    /// different distance, so they are rebased to "plies from this node" on the way in.
    /// </summary>
    private static int ToTableScore(int score, int ply) {
        if (score >= SearchScores.MateThreshold) return score + ply;
        if (score <= -SearchScores.MateThreshold) return score - ply;
        return score;
    }

    private static int FromTableScore(int score, int ply) {
        if (score >= SearchScores.MateThreshold) return score - ply;
        if (score <= -SearchScores.MateThreshold) return score + ply;
        return score;
    }

    private struct Entry {
        public uint Key;
        public ushort Move;
        public short Score;
        public short Depth;
        public ScoreBound Bound;
        public byte Generation;
    }
}
