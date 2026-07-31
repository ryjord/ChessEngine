namespace Engine.Chess.Board;

/// <summary>The four castling privileges, tracked as a bit set on the position.</summary>
[Flags]
public enum CastlingRights : byte {
    None = 0,
    WhiteKingside = 1,
    WhiteQueenside = 2,
    BlackKingside = 4,
    BlackQueenside = 8,
    White = WhiteKingside | WhiteQueenside,
    Black = BlackKingside | BlackQueenside,
    All = White | Black,
}
