using System.Text;
using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Play;

namespace Engine.Chess.Notation;

/// <summary>
/// Portable Game Notation: the interchange format every chess program reads, so a
/// game played here can be pasted straight into an analysis board elsewhere.
/// </summary>
public static class Pgn {
    private const int WrapColumn = 80;

    public static string Export(ChessGame game, string whiteName, string blackName, string? eventName = null) {
        var pgn = new StringBuilder();

        AppendTag(pgn, "Event", eventName ?? "Casual game");
        AppendTag(pgn, "Site", "Engine.Chess");
        AppendTag(pgn, "Date", DateTime.UtcNow.ToString("yyyy.MM.dd"));
        AppendTag(pgn, "Round", "-");
        AppendTag(pgn, "White", whiteName);
        AppendTag(pgn, "Black", blackName);
        AppendTag(pgn, "Result", game.Result.ToScoreString());

        // A game that did not start from the initial position is unreadable without
        // these two tags, and readers ignore them otherwise.
        if (game.StartingFen != Position.StartingFen) {
            AppendTag(pgn, "SetUp", "1");
            AppendTag(pgn, "FEN", game.StartingFen);
        }

        pgn.Append('\n');
        pgn.Append(BuildMoveText(game));
        return pgn.ToString();
    }

    /// <summary>
    /// Reads the moves out of PGN text, ignoring tags, comments and variations.
    /// Returns the moves played; anything that does not parse stops the import.
    /// </summary>
    public static IReadOnlyList<Move> ParseMoves(string pgn, Position start) {
        var position = start.Clone();
        var moves = new List<Move>();

        foreach (string token in Tokenise(pgn)) {
            Move move = San.Parse(position, token);
            if (move.IsNull) break;
            moves.Add(move);
            position.MakeMove(move);
        }
        return moves;
    }

    private static string BuildMoveText(ChessGame game) {
        var text = new StringBuilder();
        var line = new StringBuilder();

        foreach (PlayedMove played in game.History) {
            string entry = played.Side == Color.White
                ? $"{played.MoveNumber}. {played.San}"
                : line.Length == 0 ? $"{played.MoveNumber}... {played.San}" : played.San;

            if (line.Length + entry.Length + 1 > WrapColumn) {
                text.Append(line).Append('\n');
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(entry);
        }

        string result = game.Result.ToScoreString();
        if (line.Length + result.Length + 1 > WrapColumn) {
            text.Append(line).Append('\n');
            line.Clear();
        }
        if (line.Length > 0) line.Append(' ');
        line.Append(result);

        return text.Append(line).Append('\n').ToString();
    }

    private static void AppendTag(StringBuilder pgn, string name, string value) =>
        pgn.Append('[').Append(name).Append(" \"").Append(value.Replace("\"", "'")).Append("\"]\n");

    private static IEnumerable<string> Tokenise(string pgn) {
        int depth = 0;
        var token = new StringBuilder();

        for (int i = 0; i < pgn.Length; i++) {
            char symbol = pgn[i];

            // Skip tag pairs, brace comments and parenthesised variations wholesale.
            if (symbol is '[' or '{' or '(') {
                depth++;
                continue;
            }
            if (symbol is ']' or '}' or ')') {
                depth--;
                continue;
            }
            if (depth > 0) continue;

            // A semicolon comments out the rest of the line.
            if (symbol == ';') {
                while (i < pgn.Length && pgn[i] != '\n') i++;
                continue;
            }

            if (!char.IsWhiteSpace(symbol)) {
                token.Append(symbol);
                continue;
            }

            if (token.Length > 0) {
                if (IsMoveToken(token.ToString())) yield return StripMoveNumber(token.ToString());
                token.Clear();
            }
        }

        if (token.Length > 0 && IsMoveToken(token.ToString())) yield return StripMoveNumber(token.ToString());
    }

    private static bool IsMoveToken(string token) =>
        token is not ("1-0" or "0-1" or "1/2-1/2" or "*") &&
        token.Length > 0 &&
        !token.All(symbol => char.IsDigit(symbol) || symbol == '.');

    /// <summary>Handles "1.e4" written without a space after the move number.</summary>
    private static string StripMoveNumber(string token) {
        int lastDot = token.LastIndexOf('.');
        return lastDot >= 0 && lastDot < token.Length - 1 ? token[(lastDot + 1)..] : token;
    }
}
