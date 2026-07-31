namespace Engine.Chess.Board;

/// <summary>How a game stands, or how it finished.</summary>
public enum GameResult {
    InProgress,
    WhiteWinsByCheckmate,
    BlackWinsByCheckmate,
    DrawByStalemate,
    DrawByFiftyMoveRule,
    DrawByRepetition,
    DrawByInsufficientMaterial,
    /// <summary>A player ran out of time and the opponent had mating material.</summary>
    WhiteWinsOnTime,
    BlackWinsOnTime,
    WhiteResigned,
    BlackResigned,
    DrawByAgreement,
}

public static class GameResults {
    public static bool IsOver(this GameResult result) => result != GameResult.InProgress;

    public static bool IsDraw(this GameResult result) => result is
        GameResult.DrawByStalemate or GameResult.DrawByFiftyMoveRule or GameResult.DrawByRepetition or
        GameResult.DrawByInsufficientMaterial or GameResult.DrawByAgreement;

    /// <summary>The PGN result tag: <c>1-0</c>, <c>0-1</c>, <c>1/2-1/2</c> or <c>*</c>.</summary>
    public static string ToScoreString(this GameResult result) => result switch {
        GameResult.InProgress => "*",
        GameResult.WhiteWinsByCheckmate or GameResult.WhiteWinsOnTime or GameResult.BlackResigned => "1-0",
        GameResult.BlackWinsByCheckmate or GameResult.BlackWinsOnTime or GameResult.WhiteResigned => "0-1",
        _ => "1/2-1/2",
    };

    /// <summary>A short phrase for the game-over banner, for example "White wins by checkmate".</summary>
    public static string ToDescription(this GameResult result) => result switch {
        GameResult.InProgress => "Game in progress",
        GameResult.WhiteWinsByCheckmate => "White wins by checkmate",
        GameResult.BlackWinsByCheckmate => "Black wins by checkmate",
        GameResult.WhiteWinsOnTime => "White wins on time",
        GameResult.BlackWinsOnTime => "Black wins on time",
        GameResult.WhiteResigned => "Black wins by resignation",
        GameResult.BlackResigned => "White wins by resignation",
        GameResult.DrawByStalemate => "Draw by stalemate",
        GameResult.DrawByFiftyMoveRule => "Draw by the fifty-move rule",
        GameResult.DrawByRepetition => "Draw by threefold repetition",
        GameResult.DrawByInsufficientMaterial => "Draw by insufficient material",
        GameResult.DrawByAgreement => "Draw by agreement",
        _ => "Game over",
    };
}
