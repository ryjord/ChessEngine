using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Evaluation;
using Engine.Chess.Notation;

namespace Engine.Chess.Play;

/// <summary>One played move, with everything needed to render it without re-deriving it.</summary>
public sealed record PlayedMove {
    public required Move Move { get; init; }

    public required string San { get; init; }

    public required Color Side { get; init; }

    /// <summary>Full move number, so the move list can be numbered without counting.</summary>
    public required int MoveNumber { get; init; }

    /// <summary>The position before the move, so the board can be rewound to any point.</summary>
    public required string FenBefore { get; init; }

    public required Piece Captured { get; init; }

    public required bool IsCheck { get; init; }
}

/// <summary>
/// A game in progress: the position, the moves played, and the derived state the
/// interface needs such as captured material and whose turn it is.
/// </summary>
/// <remarks>
/// This deliberately owns no search and no timers. Keeping the rules of the game
/// separate from who is thinking about them is what lets the same type back a
/// human-versus-bot game, a review of a finished game, and the tests.
/// </remarks>
public sealed class ChessGame {
    private readonly List<PlayedMove> _history = [];
    private readonly string _startingFen;

    public ChessGame(string? startingFen = null) {
        _startingFen = startingFen ?? Position.StartingFen;
        Position = new Position(_startingFen);
    }

    public Position Position { get; private set; }

    public IReadOnlyList<PlayedMove> History => _history;

    public Color SideToMove => Position.SideToMove;

    /// <summary>Set when the game ends for a reason the position alone cannot express, such as resignation.</summary>
    public GameResult? DeclaredResult { get; private set; }

    public GameResult Result => DeclaredResult ?? Position.Result();

    public bool IsOver => Result.IsOver();

    public string StartingFen => _startingFen;

    /// <summary>The move just played, used to highlight its squares on the board.</summary>
    public PlayedMove? LastMove => _history.Count > 0 ? _history[^1] : null;

    public IReadOnlyList<Move> MoveSequence => _history.Select(entry => entry.Move).ToList();

    /// <summary>Material advantage in centipawns from white's point of view.</summary>
    public int MaterialBalance => Evaluator.MaterialBalance(Position);

    /// <summary>
    /// Plays a move, rejecting anything illegal rather than corrupting the position.
    /// Returns false if the move is not legal here or the game is already over.
    /// </summary>
    public bool TryMakeMove(Move move) {
        if (IsOver) return false;

        MoveList legal = Position.LegalMoves();
        Move matched = Move.None;
        for (int i = 0; i < legal.Count; i++) {
            if (legal[i] == move) {
                matched = legal[i];
                break;
            }
        }
        if (matched.IsNull) return false;

        _history.Add(new PlayedMove {
            Move = matched,
            San = San.ToSan(Position, matched),
            Side = Position.SideToMove,
            MoveNumber = Position.FullmoveNumber,
            FenBefore = Position.ToFen(),
            Captured = matched.IsEnPassant
                ? Pieces.Create(Position.SideToMove.Opponent(), PieceType.Pawn)
                : Position.PieceAt(matched.To),
            IsCheck = GivesCheck(matched),
        });

        Position.MakeMove(matched);
        return true;
    }

    /// <summary>Plays a move written in SAN. Returns false if it does not match a legal move.</summary>
    public bool TryMakeSanMove(string san) {
        Move move = San.Parse(Position, san);
        return !move.IsNull && TryMakeMove(move);
    }

    /// <summary>
    /// Finds the legal move from one square to another. Returns every matching move
    /// when the destination is a promotion, so the caller can ask which piece to
    /// promote to rather than guessing.
    /// </summary>
    public IReadOnlyList<Move> MovesBetween(int from, int to) {
        MoveList legal = Position.LegalMoves();
        var matches = new List<Move>(4);
        for (int i = 0; i < legal.Count; i++) {
            if (legal[i].From == from && legal[i].To == to) matches.Add(legal[i]);
        }
        return matches;
    }

    /// <summary>Legal destinations for a piece, as a bitboard, for highlighting the board.</summary>
    public ulong DestinationsFrom(int square) {
        Piece piece = Position.PieceAt(square);
        if (piece == Piece.None || piece.ColorOf() != Position.SideToMove) return 0UL;
        return Position.LegalDestinations(square);
    }

    /// <summary>Takes back a single ply. Also clears any declared result, since the game is live again.</summary>
    public bool TryUndo() {
        if (_history.Count == 0) return false;
        Position.UnmakeMove();
        _history.RemoveAt(_history.Count - 1);
        DeclaredResult = null;
        return true;
    }

    /// <summary>Takes back the player's move and the bot's reply together, which is what "undo" means in a game.</summary>
    public int UndoFullMove() {
        int undone = 0;
        if (TryUndo()) undone++;
        if (TryUndo()) undone++;
        return undone;
    }

    public void Resign(Color side) =>
        DeclaredResult = side == Color.White ? GameResult.WhiteResigned : GameResult.BlackResigned;

    public void DeclareTimeout(Color side) {
        // A flag fall is only a loss if the opponent could actually deliver mate.
        Color winner = side.Opponent();
        bool winnerCanMate = !IsBareKing(winner);
        DeclaredResult = !winnerCanMate
            ? GameResult.DrawByInsufficientMaterial
            : winner == Color.White ? GameResult.WhiteWinsOnTime : GameResult.BlackWinsOnTime;
    }

    public void AgreeDraw() => DeclaredResult = GameResult.DrawByAgreement;

    public void Reset(string? startingFen = null) {
        Position = new Position(startingFen ?? _startingFen);
        _history.Clear();
        DeclaredResult = null;
    }

    /// <summary>Rebuilds the position as it stood after a given number of plies, for browsing the game.</summary>
    public Position PositionAfterPly(int ply) {
        var replay = new Position(_startingFen);
        for (int i = 0; i < Math.Min(ply, _history.Count); i++) replay.MakeMove(_history[i].Move);
        return replay;
    }

    /// <summary>Pieces of the given colour that have been captured, heaviest first.</summary>
    public IReadOnlyList<PieceType> CapturedBy(Color capturer) => _history
        .Where(entry => entry.Side == capturer && entry.Captured != Piece.None)
        .Select(entry => entry.Captured.TypeOf())
        .OrderByDescending(type => Evaluator.PieceValue[(int)type])
        .ToList();

    public string ToPgn(string whiteName, string blackName, string? eventName = null) =>
        Pgn.Export(this, whiteName, blackName, eventName);

    private bool IsBareKing(Color side) =>
        (Position.Bitboard(side) & ~Position.Bitboard(side, PieceType.King)) == 0;

    private bool GivesCheck(Move move) {
        Position.MakeMove(move);
        bool check = Position.IsInCheck;
        Position.UnmakeMove();
        return check;
    }
}
