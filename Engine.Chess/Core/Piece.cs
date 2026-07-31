namespace Engine.Chess.Core;

/// <summary>The side to move. Values double as indices into per-colour arrays.</summary>
public enum Color : byte {
    White = 0,
    Black = 1,
}

/// <summary>A piece kind, independent of colour.</summary>
public enum PieceType : byte {
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6,
}

/// <summary>
/// A coloured piece. White occupies 1..6 and black 7..12 so that a piece can be
/// split into its colour and type with a single comparison.
/// </summary>
public enum Piece : byte {
    None = 0,
    WhitePawn = 1,
    WhiteKnight = 2,
    WhiteBishop = 3,
    WhiteRook = 4,
    WhiteQueen = 5,
    WhiteKing = 6,
    BlackPawn = 7,
    BlackKnight = 8,
    BlackBishop = 9,
    BlackRook = 10,
    BlackQueen = 11,
    BlackKing = 12,
}

public static class Pieces {
    /// <summary>Number of distinct coloured pieces, used to size bitboard arrays.</summary>
    public const int Count = 12;

    private static readonly char[] Symbols = " PNBRQKpnbrqk".ToCharArray();

    public static Color ColorOf(this Piece piece) => piece >= Piece.BlackPawn ? Color.Black : Color.White;

    public static PieceType TypeOf(this Piece piece) =>
        piece == Piece.None ? PieceType.None
                            : (PieceType)(piece >= Piece.BlackPawn ? (int)piece - 6 : (int)piece);

    public static Piece Create(Color color, PieceType type) =>
        type == PieceType.None ? Piece.None : (Piece)((int)type + (color == Color.Black ? 6 : 0));

    /// <summary>Index into the flat 12-entry bitboard array. Undefined for <see cref="Piece.None"/>.</summary>
    public static int BitboardIndex(this Piece piece) => (int)piece - 1;

    public static int BitboardIndex(Color color, PieceType type) => (int)color * 6 + (int)type - 1;

    /// <summary>FEN letter for the piece: uppercase for white, lowercase for black.</summary>
    public static char ToChar(this Piece piece) => Symbols[(int)piece];

    public static Piece FromChar(char symbol) {
        int index = Array.IndexOf(Symbols, symbol);
        return index <= 0 ? Piece.None : (Piece)index;
    }

    /// <summary>Algebraic letter used in SAN. Pawns have no letter.</summary>
    public static string SanLetter(this PieceType type) => type switch {
        PieceType.Knight => "N",
        PieceType.Bishop => "B",
        PieceType.Rook => "R",
        PieceType.Queen => "Q",
        PieceType.King => "K",
        _ => string.Empty,
    };

    public static Color Opponent(this Color color) => (Color)(1 - (int)color);
}
