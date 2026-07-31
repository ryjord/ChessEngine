using System.Text;
using Engine.Chess.Board;
using Engine.Chess.Core;

namespace Engine.Chess.Notation;

/// <summary>Reads and writes Forsyth-Edwards Notation, the standard text form of a position.</summary>
public static class Fen {
    /// <summary>Throws <see cref="FormatException"/> if the text is not a well-formed FEN.</summary>
    public static void Load(Position position, string fen) {
        if (string.IsNullOrWhiteSpace(fen)) throw new FormatException("FEN string is empty.");

        string[] fields = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2) throw new FormatException($"FEN needs at least a board and a side to move: '{fen}'.");

        position.Clear();
        ReadBoard(position, fields[0]);

        position.SideToMove = fields[1] switch {
            "w" => Color.White,
            "b" => Color.Black,
            _ => throw new FormatException($"Side to move must be 'w' or 'b', got '{fields[1]}'."),
        };

        position.Castling = fields.Length > 2 ? ReadCastling(fields[2]) : CastlingRights.None;

        position.EnPassantSquare = fields.Length > 3 && fields[3] != "-"
            ? Squares.FromName(fields[3])
            : Squares.None;

        position.HalfmoveClock = fields.Length > 4 && int.TryParse(fields[4], out int halfmoves) ? halfmoves : 0;
        position.FullmoveNumber = fields.Length > 5 && int.TryParse(fields[5], out int fullmoves) ? fullmoves : 1;

        Validate(position);
        position.RefreshHash();
    }

    public static string Export(Position position) {
        var fen = new StringBuilder(90);

        for (int rank = 7; rank >= 0; rank--) {
            int emptyRun = 0;
            for (int file = 0; file < 8; file++) {
                Piece piece = position.PieceAt(Squares.From(file, rank));
                if (piece == Piece.None) {
                    emptyRun++;
                    continue;
                }
                if (emptyRun > 0) {
                    fen.Append(emptyRun);
                    emptyRun = 0;
                }
                fen.Append(piece.ToChar());
            }
            if (emptyRun > 0) fen.Append(emptyRun);
            if (rank > 0) fen.Append('/');
        }

        fen.Append(position.SideToMove == Color.White ? " w " : " b ");
        fen.Append(WriteCastling(position.Castling)).Append(' ');
        fen.Append(position.EnPassantSquare == Squares.None ? "-" : Squares.ToName(position.EnPassantSquare));
        fen.Append(' ').Append(position.HalfmoveClock);
        fen.Append(' ').Append(position.FullmoveNumber);
        return fen.ToString();
    }

    private static void ReadBoard(Position position, string board) {
        int rank = 7;
        int file = 0;

        foreach (char symbol in board) {
            if (symbol == '/') {
                if (file != 8) throw new FormatException($"Rank {rank + 1} does not describe eight squares.");
                rank--;
                file = 0;
                continue;
            }

            if (char.IsDigit(symbol)) {
                file += symbol - '0';
                if (file > 8) throw new FormatException($"Rank {rank + 1} overflows past the h-file.");
                continue;
            }

            Piece piece = Pieces.FromChar(symbol);
            if (piece == Piece.None) throw new FormatException($"'{symbol}' is not a piece letter.");
            if (rank < 0 || file > 7) throw new FormatException("Board description runs past a1.");

            position.AddPiece(piece, Squares.From(file, rank));
            file++;
        }

        if (rank != 0 || file != 8) throw new FormatException("Board description must cover all eight ranks.");
    }

    private static CastlingRights ReadCastling(string field) {
        if (field == "-") return CastlingRights.None;

        CastlingRights rights = CastlingRights.None;
        foreach (char symbol in field) {
            rights |= symbol switch {
                'K' => CastlingRights.WhiteKingside,
                'Q' => CastlingRights.WhiteQueenside,
                'k' => CastlingRights.BlackKingside,
                'q' => CastlingRights.BlackQueenside,
                _ => throw new FormatException($"'{symbol}' is not a castling letter."),
            };
        }
        return rights;
    }

    private static string WriteCastling(CastlingRights rights) {
        if (rights == CastlingRights.None) return "-";

        var text = new StringBuilder(4);
        if (rights.HasFlag(CastlingRights.WhiteKingside)) text.Append('K');
        if (rights.HasFlag(CastlingRights.WhiteQueenside)) text.Append('Q');
        if (rights.HasFlag(CastlingRights.BlackKingside)) text.Append('k');
        if (rights.HasFlag(CastlingRights.BlackQueenside)) text.Append('q');
        return text.ToString();
    }

    /// <summary>
    /// Rejects positions the engine cannot reason about: a missing or duplicated
    /// king, or a side to move that could capture the enemy king.
    /// </summary>
    private static void Validate(Position position) {
        foreach (Color color in new[] { Color.White, Color.Black }) {
            int kings = Bitboards.PopCount(position.Bitboard(color, PieceType.King));
            if (kings != 1) throw new FormatException($"{color} must have exactly one king, found {kings}.");
        }

        Color opponent = position.SideToMove.Opponent();
        if (position.IsAttacked(position.KingSquare(opponent), position.SideToMove)) {
            throw new FormatException("The side that just moved has left its king in check.");
        }
    }
}
