using System.Text;
using Engine.Chess.Board;
using Engine.Chess.Core;

namespace Engine.Chess.Notation;

/// <summary>
/// Standard Algebraic Notation, the human-readable move format used in PGN and in
/// the move list. Both directions need the position the move is played from,
/// because SAN omits anything the reader can infer from the board.
/// </summary>
public static class San {
    /// <summary>
    /// Renders a legal move as SAN. The move must be legal in
    /// <paramref name="position"/>; it is played and taken back to work out
    /// whether it gives check or mate.
    /// </summary>
    public static string ToSan(Position position, Move move) {
        if (move.IsNull) return "--";

        var san = new StringBuilder(8);

        if (move.Flag == MoveFlag.KingsideCastle) {
            san.Append("O-O");
        } else if (move.Flag == MoveFlag.QueensideCastle) {
            san.Append("O-O-O");
        } else {
            PieceType moving = position.PieceAt(move.From).TypeOf();

            if (moving == PieceType.Pawn) {
                // A capturing pawn is identified by the file it came from.
                if (move.IsCapture) san.Append((char)('a' + Squares.FileOf(move.From))).Append('x');
                san.Append(Squares.ToName(move.To));
                if (move.IsPromotion) san.Append('=').Append(Pieces.Create(Color.White, move.PromotionPiece).ToChar());
            } else {
                san.Append(moving.SanLetter());
                san.Append(Disambiguate(position, move, moving));
                if (move.IsCapture) san.Append('x');
                san.Append(Squares.ToName(move.To));
            }
        }

        position.MakeMove(move);
        bool inCheck = position.IsInCheck;
        bool hasReply = position.LegalMoves().Count > 0;
        position.UnmakeMove();

        if (inCheck) san.Append(hasReply ? '+' : '#');
        return san.ToString();
    }

    /// <summary>
    /// Parses SAN against a position, returning <see cref="Move.None"/> when the
    /// text matches no legal move. Tolerant of the usual decorations: check and
    /// mate marks, <c>e.p.</c>, and <c>0-0</c> written with zeroes.
    /// </summary>
    public static Move Parse(Position position, string san) {
        string text = Normalise(san);
        if (text.Length == 0) return Move.None;

        MoveList legal = position.LegalMoves();
        for (int i = 0; i < legal.Count; i++) {
            if (Normalise(ToSan(position, legal[i])) == text) return legal[i];
        }
        return Move.None;
    }

    /// <summary>Renders a whole line, numbering it from the position it starts in.</summary>
    public static string ToSanLine(Position position, IReadOnlyList<Move> moves) {
        var line = new StringBuilder();
        var replay = position.Clone();
        int moveNumber = replay.FullmoveNumber;
        bool whiteToMove = replay.SideToMove == Color.White;

        foreach (Move move in moves) {
            if (whiteToMove) line.Append(moveNumber).Append(". ");
            else if (line.Length == 0) line.Append(moveNumber).Append("... ");

            line.Append(ToSan(replay, move)).Append(' ');
            replay.MakeMove(move);

            if (!whiteToMove) moveNumber++;
            whiteToMove = !whiteToMove;
        }
        return line.ToString().TrimEnd();
    }

    private static string Disambiguate(Position position, Move move, PieceType moving) {
        MoveList legal = position.LegalMoves();
        bool needsFile = false;
        bool needsRank = false;
        bool ambiguous = false;

        for (int i = 0; i < legal.Count; i++) {
            Move other = legal[i];
            if (other == move || other.To != move.To) continue;
            if (position.PieceAt(other.From).TypeOf() != moving) continue;

            ambiguous = true;
            if (Squares.FileOf(other.From) == Squares.FileOf(move.From)) needsRank = true;
            if (Squares.RankOf(other.From) == Squares.RankOf(move.From)) needsFile = true;
        }

        if (!ambiguous) return string.Empty;
        // File alone is the preferred discriminator; rank is used only when the
        // rivals share a file, and both when they share neither uniquely.
        if (!needsRank) return ((char)('a' + Squares.FileOf(move.From))).ToString();
        if (!needsFile) return (Squares.RankOf(move.From) + 1).ToString();
        return Squares.ToName(move.From);
    }

    private static string Normalise(string san) {
        var text = new StringBuilder(san.Length);
        foreach (char symbol in san) {
            switch (symbol) {
                case '+' or '#' or '!' or '?' or ' ' or '.' or '-':
                    continue;
                case '0':
                    text.Append('O');
                    break;
                default:
                    text.Append(symbol);
                    break;
            }
        }
        // "e.p." collapses to "ep" once dots are stripped.
        return text.ToString().Replace("ep", string.Empty, StringComparison.Ordinal);
    }
}
