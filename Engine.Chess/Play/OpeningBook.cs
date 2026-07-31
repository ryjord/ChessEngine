using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Notation;

namespace Engine.Chess.Play;

/// <summary>
/// A small book of mainline openings. Without one, a fixed-depth engine answers
/// 1.e4 with the same move every game, which gets dull fast; the book also saves
/// the search from spending time on positions where theory already has an answer.
/// </summary>
/// <remarks>
/// Lines are stored as text and replayed at startup, so every book move is checked
/// for legality by the move generator rather than trusted. A malformed line would
/// surface immediately as a missing opening rather than an illegal move on the board.
/// </remarks>
public sealed class OpeningBook {
    private readonly Dictionary<ulong, List<Move>> _positions = [];

    private OpeningBook(IEnumerable<string> lines) {
        foreach (string line in lines) Add(line);
    }

    /// <summary>The shared book, built once on first use.</summary>
    public static OpeningBook Default { get; } = new(Lines);

    public int PositionCount => _positions.Count;

    public bool TryGetMoves(Position position, out IReadOnlyList<Move> moves) {
        if (_positions.TryGetValue(position.ZobristKey, out List<Move>? found)) {
            moves = found;
            return true;
        }
        moves = [];
        return false;
    }

    /// <summary>Picks a book move at random, or <see cref="Move.None"/> if the position is out of book.</summary>
    public Move Choose(Position position, Random random) {
        if (!TryGetMoves(position, out IReadOnlyList<Move> moves) || moves.Count == 0) return Move.None;
        return moves[random.Next(moves.Count)];
    }

    private void Add(string line) {
        var position = new Position();
        foreach (string token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
            Move move = San.Parse(position, token);
            if (move.IsNull) return; // A bad line is dropped rather than corrupting the book.

            if (!_positions.TryGetValue(position.ZobristKey, out List<Move>? moves)) {
                moves = [];
                _positions[position.ZobristKey] = moves;
            }
            if (!moves.Contains(move)) moves.Add(move);

            position.MakeMove(move);
        }
    }

    /// <summary>
    /// Mainlines of the openings a club player is most likely to meet. Declared as a
    /// property so it does not depend on static field initialisation order.
    /// </summary>
    private static string[] Lines => [
        // Open games
        "e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1 b5 Bb3 d6 c3 O-O",   // Ruy Lopez, closed
        "e4 e5 Nf3 Nc6 Bb5 a6 Bxc6 dxc6 O-O f6 d4 exd4 Nxd4 c5",       // Ruy Lopez, exchange
        "e4 e5 Nf3 Nc6 Bc4 Bc5 c3 Nf6 d4 exd4 cxd4 Bb4+ Bd2 Bxd2+",    // Italian, Giuoco Piano
        "e4 e5 Nf3 Nc6 Bc4 Nf6 d3 Bc5 c3 d6 O-O O-O Re1 a6",           // Italian, Giuoco Pianissimo
        "e4 e5 Nf3 Nc6 d4 exd4 Nxd4 Nf6 Nxc6 bxc6 e5 Qe7 Qe2 Nd5",     // Scotch
        "e4 e5 Nf3 Nf6 Nxe5 d6 Nf3 Nxe4 d4 d5 Bd3 Be7 O-O Nc6",        // Petrov
        "e4 e5 Nc3 Nf6 f4 d5 fxe5 Nxe4 Nf3 Be7 d4 O-O",                // Vienna
        "e4 e5 f4 exf4 Nf3 g5 h4 g4 Ne5 Nf6 d4 d6",                    // King's Gambit

        // Sicilian
        "e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6 Be3 e5 Nb3 Be6",         // Najdorf
        "e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 g6 Be3 Bg7 f3 O-O",         // Dragon
        "e4 c5 Nf3 e6 d4 cxd4 Nxd4 Nc6 Nc3 Qc7 Be3 a6 Bd3 Nf6",        // Taimanov
        "e4 c5 Nf3 Nc6 d4 cxd4 Nxd4 g6 c4 Nf6 Nc3 d6 Be2 Nxd4",        // Accelerated Dragon
        "e4 c5 Nc3 Nc6 g3 g6 Bg2 Bg7 d3 d6 f4 e6 Nf3 Nge7",            // Closed Sicilian
        "e4 c5 Nf3 d6 Bb5+ Bd7 Bxd7+ Qxd7 O-O Nc6 c3 Nf6 Re1 e6",      // Moscow

        // Semi-open
        "e4 e6 d4 d5 Nc3 Bb4 e5 c5 a3 Bxc3+ bxc3 Ne7 Qg4 O-O",         // French, Winawer
        "e4 e6 d4 d5 Nd2 Nf6 e5 Nfd7 Bd3 c5 c3 Nc6 Ne2 cxd4",          // French, Tarrasch
        "e4 c6 d4 d5 Nc3 dxe4 Nxe4 Bf5 Ng3 Bg6 h4 h6 Nf3 Nd7",         // Caro-Kann, classical
        "e4 c6 d4 d5 e5 Bf5 Nf3 e6 Be2 c5 Be3 Qb6 Nc3 Nc6",            // Caro-Kann, advance
        "e4 d5 exd5 Qxd5 Nc3 Qa5 d4 Nf6 Nf3 c6 Bc4 Bf5 Bd2 e6",        // Scandinavian
        "e4 Nf6 e5 Nd5 d4 d6 Nf3 Bg4 Be2 e6 O-O Be7 c4 Nb6",           // Alekhine
        "e4 d6 d4 Nf6 Nc3 g6 Nf3 Bg7 Be2 O-O O-O c6",                  // Pirc

        // Queen's pawn
        "d4 d5 c4 e6 Nc3 Nf6 Bg5 Be7 e3 O-O Nf3 h6 Bh4 b6",            // QGD, orthodox
        "d4 d5 c4 dxc4 Nf3 Nf6 e3 e6 Bxc4 c5 O-O a6 a4 Nc6",           // QGA
        "d4 d5 c4 c6 Nf3 Nf6 Nc3 dxc4 a4 Bf5 e3 e6 Bxc4 Bb4",          // Slav
        "d4 Nf6 c4 e6 Nc3 Bb4 e3 O-O Bd3 d5 Nf3 c5 O-O Nc6",           // Nimzo-Indian
        "d4 Nf6 c4 g6 Nc3 Bg7 e4 d6 Nf3 O-O Be2 e5 O-O Nc6",           // King's Indian
        "d4 Nf6 c4 e6 Nf3 b6 g3 Bb7 Bg2 Be7 O-O O-O Nc3 Ne4",          // Queen's Indian
        "d4 Nf6 c4 g6 Nc3 d5 cxd5 Nxd5 e4 Nxc3 bxc3 Bg7 Nf3 c5",       // Gruenfeld
        "d4 f5 g3 Nf6 Bg2 g6 Nf3 Bg7 O-O O-O c4 d6 Nc3 Qe8",           // Dutch, Leningrad

        // Flank
        "c4 e5 Nc3 Nf6 Nf3 Nc6 g3 d5 cxd5 Nxd5 Bg2 Nb6 O-O Be7",       // English, reversed Sicilian
        "c4 Nf6 Nc3 e6 Nf3 d5 d4 Be7 Bg5 O-O e3 h6",                   // English into QGD
        "Nf3 d5 g3 Nf6 Bg2 e6 O-O Be7 d3 O-O Nbd2 c5",                 // King's Indian Attack
    ];
}
