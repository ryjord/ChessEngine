using System.Runtime.CompilerServices;
using Engine.Chess.Core;
using Engine.Chess.Notation;

namespace Engine.Chess.Board;

/// <summary>
/// A chess position: twelve piece bitboards kept in step with a 64-entry mailbox,
/// plus the state needed to play and unplay moves. Moves are applied in place and
/// reverted from an undo stack rather than by copying, which is what makes the
/// search affordable.
/// </summary>
public sealed class Position {
    public const string StartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>Rights that survive a move touching each square; indexed by square.</summary>
    private static readonly CastlingRights[] CastlingMask = BuildCastlingMask();

    private readonly ulong[] _pieces = new ulong[Pieces.Count];
    private readonly ulong[] _colors = new ulong[2];
    private readonly Piece[] _board = new Piece[Squares.Count];

    private UndoInfo[] _undoStack = new UndoInfo[512];
    private int _undoCount;

    /// <summary>Hashes of every position reached, oldest first, used for repetition detection.</summary>
    private ulong[] _hashHistory = new ulong[512];
    private int _hashCount;

    public Position() : this(StartingFen) { }

    public Position(string fen) {
        Attacks.Initialize();
        Fen.Load(this, fen);
    }

    public ulong Occupied { get; private set; }

    public Color SideToMove { get; internal set; }

    public CastlingRights Castling { get; internal set; }

    /// <summary>The square a pawn may capture onto, or <see cref="Squares.None"/>.</summary>
    public int EnPassantSquare { get; internal set; } = Squares.None;

    /// <summary>Plies since the last capture or pawn move, for the fifty-move rule.</summary>
    public int HalfmoveClock { get; internal set; }

    public int FullmoveNumber { get; internal set; } = 1;

    public ulong ZobristKey { get; private set; }

    /// <summary>Plies played since the position was created, used to index search-local tables.</summary>
    public int Ply => _undoCount;

    public Piece PieceAt(int square) => _board[square];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Bitboard(Color color, PieceType type) => _pieces[Pieces.BitboardIndex(color, type)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Bitboard(Color color) => _colors[(int)color];

    /// <summary>Every piece of the given type, both colours.</summary>
    public ulong Bitboard(PieceType type) =>
        _pieces[Pieces.BitboardIndex(Color.White, type)] | _pieces[Pieces.BitboardIndex(Color.Black, type)];

    public int KingSquare(Color color) =>
        Bitboards.LeastSignificant(_pieces[Pieces.BitboardIndex(color, PieceType.King)]);

    public int PieceCount => Bitboards.PopCount(Occupied);

    public bool IsInCheck => IsAttacked(KingSquare(SideToMove), SideToMove.Opponent());

    // ---------------------------------------------------------------- board edits

    internal void Clear() {
        Array.Clear(_pieces);
        Array.Clear(_colors);
        Array.Fill(_board, Piece.None);
        Occupied = 0;
        SideToMove = Color.White;
        Castling = CastlingRights.None;
        EnPassantSquare = Squares.None;
        HalfmoveClock = 0;
        FullmoveNumber = 1;
        ZobristKey = 0;
        _undoCount = 0;
        _hashCount = 0;
    }

    internal void AddPiece(Piece piece, int square) {
        ulong mask = Bitboards.Square(square);
        _pieces[piece.BitboardIndex()] |= mask;
        _colors[(int)piece.ColorOf()] |= mask;
        Occupied |= mask;
        _board[square] = piece;
        ZobristKey ^= Zobrist.PieceSquare[piece.BitboardIndex(), square];
    }

    private void RemovePiece(int square) {
        Piece piece = _board[square];
        ulong mask = Bitboards.Square(square);
        _pieces[piece.BitboardIndex()] &= ~mask;
        _colors[(int)piece.ColorOf()] &= ~mask;
        Occupied &= ~mask;
        _board[square] = Piece.None;
        ZobristKey ^= Zobrist.PieceSquare[piece.BitboardIndex(), square];
    }

    private void MovePiece(int from, int to) {
        Piece piece = _board[from];
        ulong mask = Bitboards.Square(from) | Bitboards.Square(to);
        _pieces[piece.BitboardIndex()] ^= mask;
        _colors[(int)piece.ColorOf()] ^= mask;
        Occupied ^= mask;
        _board[from] = Piece.None;
        _board[to] = piece;
        ZobristKey ^= Zobrist.PieceSquare[piece.BitboardIndex(), from]
                    ^ Zobrist.PieceSquare[piece.BitboardIndex(), to];
    }

    /// <summary>Recomputes the hash from scratch. Called after loading a FEN.</summary>
    internal void RefreshHash() {
        ulong key = 0;
        for (int square = 0; square < Squares.Count; square++) {
            Piece piece = _board[square];
            if (piece != Piece.None) key ^= Zobrist.PieceSquare[piece.BitboardIndex(), square];
        }
        key ^= Zobrist.Castling[(int)Castling];
        if (EnPassantSquare != Squares.None) key ^= Zobrist.EnPassantFile[Squares.FileOf(EnPassantSquare)];
        if (SideToMove == Color.Black) key ^= Zobrist.SideToMove;

        ZobristKey = key;
        _hashCount = 0;
        PushHash(key);
    }

    // ---------------------------------------------------------------- make / unmake

    public void MakeMove(Move move) {
        int from = move.From;
        int to = move.To;
        Color us = SideToMove;
        Color them = us.Opponent();
        Piece moving = _board[from];

        PushUndo(new UndoInfo {
            Captured = move.IsEnPassant ? Pieces.Create(them, PieceType.Pawn) : _board[to],
            Castling = Castling,
            EnPassantSquare = EnPassantSquare,
            HalfmoveClock = HalfmoveClock,
            Move = move,
        });

        // Roll the old castling and en-passant contributions out of the hash; the
        // new values are folded back in once they are known.
        ZobristKey ^= Zobrist.Castling[(int)Castling];
        if (EnPassantSquare != Squares.None) ZobristKey ^= Zobrist.EnPassantFile[Squares.FileOf(EnPassantSquare)];

        HalfmoveClock++;
        EnPassantSquare = Squares.None;

        if (move.IsEnPassant) {
            RemovePiece(us == Color.White ? to - 8 : to + 8);
        } else if (move.IsCapture) {
            RemovePiece(to);
        }

        MovePiece(from, to);

        switch (move.Flag) {
            case MoveFlag.DoublePawnPush:
                EnPassantSquare = us == Color.White ? to - 8 : to + 8;
                break;
            case MoveFlag.KingsideCastle:
                MovePiece(to + 1, to - 1);
                break;
            case MoveFlag.QueensideCastle:
                MovePiece(to - 2, to + 1);
                break;
        }

        if (move.IsPromotion) {
            RemovePiece(to);
            AddPiece(Pieces.Create(us, move.PromotionPiece), to);
        }

        if (moving.TypeOf() == PieceType.Pawn || move.IsCapture) HalfmoveClock = 0;

        Castling &= CastlingMask[from] & CastlingMask[to];
        ZobristKey ^= Zobrist.Castling[(int)Castling];
        if (EnPassantSquare != Squares.None) ZobristKey ^= Zobrist.EnPassantFile[Squares.FileOf(EnPassantSquare)];

        if (us == Color.Black) FullmoveNumber++;
        SideToMove = them;
        ZobristKey ^= Zobrist.SideToMove;

        PushHash(ZobristKey);
    }

    public void UnmakeMove() {
        UndoInfo undo = _undoStack[--_undoCount];
        _hashCount--;

        Move move = undo.Move;
        int from = move.From;
        int to = move.To;
        Color us = SideToMove.Opponent();

        if (us == Color.Black) FullmoveNumber--;
        SideToMove = us;

        if (move.IsPromotion) {
            RemovePiece(to);
            AddPiece(Pieces.Create(us, PieceType.Pawn), to);
        }

        MovePiece(to, from);

        switch (move.Flag) {
            case MoveFlag.KingsideCastle:
                MovePiece(to - 1, to + 1);
                break;
            case MoveFlag.QueensideCastle:
                MovePiece(to + 1, to - 2);
                break;
        }

        if (move.IsEnPassant) {
            AddPiece(undo.Captured, us == Color.White ? to - 8 : to + 8);
        } else if (undo.Captured != Piece.None) {
            AddPiece(undo.Captured, to);
        }

        Castling = undo.Castling;
        EnPassantSquare = undo.EnPassantSquare;
        HalfmoveClock = undo.HalfmoveClock;
        ZobristKey = _hashCount > 0 ? _hashHistory[_hashCount - 1] : ZobristKey;
    }

    /// <summary>Passes the turn without moving, for null-move pruning. Never call while in check.</summary>
    public void MakeNullMove() {
        PushUndo(new UndoInfo {
            Captured = Piece.None,
            Castling = Castling,
            EnPassantSquare = EnPassantSquare,
            HalfmoveClock = HalfmoveClock,
            Move = Move.None,
        });

        if (EnPassantSquare != Squares.None) ZobristKey ^= Zobrist.EnPassantFile[Squares.FileOf(EnPassantSquare)];
        EnPassantSquare = Squares.None;
        HalfmoveClock++;
        SideToMove = SideToMove.Opponent();
        ZobristKey ^= Zobrist.SideToMove;

        PushHash(ZobristKey);
    }

    public void UnmakeNullMove() {
        UndoInfo undo = _undoStack[--_undoCount];
        _hashCount--;

        SideToMove = SideToMove.Opponent();
        EnPassantSquare = undo.EnPassantSquare;
        HalfmoveClock = undo.HalfmoveClock;
        ZobristKey = _hashCount > 0 ? _hashHistory[_hashCount - 1] : ZobristKey;
    }

    // ---------------------------------------------------------------- attacks

    /// <summary>
    /// Every piece of either colour that attacks <paramref name="square"/> given an
    /// occupancy. Passing a modified occupancy lets callers ask "would this square
    /// still be attacked if these pieces moved?" without touching the board.
    /// </summary>
    public ulong AttackersTo(int square, ulong occupied) =>
        (Attacks.Pawn(square, Color.White) & _pieces[Pieces.BitboardIndex(Color.Black, PieceType.Pawn)]) |
        (Attacks.Pawn(square, Color.Black) & _pieces[Pieces.BitboardIndex(Color.White, PieceType.Pawn)]) |
        (Attacks.Knight(square) & Bitboard(PieceType.Knight)) |
        (Attacks.King(square) & Bitboard(PieceType.King)) |
        (Attacks.Bishop(square, occupied) & (Bitboard(PieceType.Bishop) | Bitboard(PieceType.Queen))) |
        (Attacks.Rook(square, occupied) & (Bitboard(PieceType.Rook) | Bitboard(PieceType.Queen)));

    public ulong AttackersTo(int square, Color byColor, ulong occupied) =>
        AttackersTo(square, occupied) & _colors[(int)byColor];

    public bool IsAttacked(int square, Color byColor) => AttackersTo(square, byColor, Occupied) != 0;

    /// <summary>Enemy pieces currently giving check to <paramref name="color"/>'s king.</summary>
    public ulong Checkers(Color color) =>
        AttackersTo(KingSquare(color), color.Opponent(), Occupied);

    // ---------------------------------------------------------------- game state

    public MoveList LegalMoves() {
        MoveList moves = default;
        MoveGenerator.Generate(this, ref moves);
        return moves;
    }

    /// <summary>Legal destinations for the piece on a square, as a bitboard. Drives board highlighting.</summary>
    public ulong LegalDestinations(int from) {
        MoveList moves = LegalMoves();
        ulong destinations = 0UL;
        for (int i = 0; i < moves.Count; i++) {
            if (moves[i].From == from) destinations |= Bitboards.Square(moves[i].To);
        }
        return destinations;
    }

    /// <summary>
    /// Squares a piece could plausibly move to, ignoring whose turn it is and
    /// ignoring check. This exists for premoves, where the player is choosing a move
    /// for a position that does not exist yet, so nothing stronger can be checked.
    /// </summary>
    public ulong PseudoDestinationsFrom(int square) {
        Piece piece = _board[square];
        if (piece == Piece.None) return 0UL;

        Color color = piece.ColorOf();
        PieceType type = piece.TypeOf();
        ulong own = _colors[(int)color];

        if (type == PieceType.Pawn) {
            bool white = color == Color.White;
            ulong mask = Bitboards.Square(square);
            ulong push = (white ? Bitboards.North(mask) : Bitboards.South(mask)) & ~Occupied;

            ulong moves = push;
            if (push != 0 && Squares.RankOf(square) == (white ? 1 : 6)) {
                moves |= (white ? Bitboards.North(push) : Bitboards.South(push)) & ~Occupied;
            }
            // Both diagonals count: whether they are captures depends on the reply.
            return moves | (Attacks.Pawn(square, color) & ~own);
        }

        ulong destinations = Attacks.Of(type, square, Occupied) & ~own;

        if (type == PieceType.King) {
            bool white = color == Color.White;
            CastlingRights kingside = white ? CastlingRights.WhiteKingside : CastlingRights.BlackKingside;
            CastlingRights queenside = white ? CastlingRights.WhiteQueenside : CastlingRights.BlackQueenside;

            if ((Castling & kingside) != 0) destinations |= Bitboards.Square(white ? Squares.G1 : Squares.G8);
            if ((Castling & queenside) != 0) destinations |= Bitboards.Square(white ? Squares.C1 : Squares.C8);
        }

        return destinations;
    }

    public GameResult Result() {
        if (LegalMoves().Count == 0) {
            return IsInCheck
                ? SideToMove == Color.White ? GameResult.BlackWinsByCheckmate : GameResult.WhiteWinsByCheckmate
                : GameResult.DrawByStalemate;
        }
        if (HalfmoveClock >= 100) return GameResult.DrawByFiftyMoveRule;
        if (IsThreefoldRepetition()) return GameResult.DrawByRepetition;
        if (IsInsufficientMaterial()) return GameResult.DrawByInsufficientMaterial;
        return GameResult.InProgress;
    }

    public bool IsGameOver() => Result() != GameResult.InProgress;

    /// <summary>True once the current position has occurred three times in the game.</summary>
    public bool IsThreefoldRepetition() => RepetitionCount() >= 3;

    /// <summary>
    /// True if the position has occurred before within the current fifty-move window.
    /// The search treats a single repeat as a draw, which is standard and avoids
    /// wasting nodes proving a line that only repeats.
    /// </summary>
    public bool IsRepetition() => RepetitionCount() >= 2;

    private int RepetitionCount() {
        int count = 0;
        // Only positions since the last irreversible move can repeat, and only
        // every second ply has the same side to move.
        int oldest = Math.Max(0, _hashCount - 1 - HalfmoveClock);
        for (int i = _hashCount - 1; i >= oldest; i -= 2) {
            if (_hashHistory[i] == ZobristKey) count++;
        }
        return count;
    }

    /// <summary>
    /// Detects material that cannot force mate: bare kings, king and minor piece,
    /// and king and bishop each with all bishops on one colour complex.
    /// </summary>
    public bool IsInsufficientMaterial() {
        if (Bitboard(PieceType.Pawn) != 0 || Bitboard(PieceType.Rook) != 0 || Bitboard(PieceType.Queen) != 0) {
            return false;
        }

        ulong bishops = Bitboard(PieceType.Bishop);
        ulong knights = Bitboard(PieceType.Knight);
        int minors = Bitboards.PopCount(bishops) + Bitboards.PopCount(knights);
        if (minors <= 1) return true;
        if (knights != 0) return false;

        return (bishops & Bitboards.LightSquares) == 0 || (bishops & Bitboards.DarkSquares) == 0;
    }

    /// <summary>True when only kings and pawns remain for the side to move, which disables null-move pruning.</summary>
    public bool HasOnlyPawns(Color color) =>
        (Bitboard(color) & ~Bitboard(color, PieceType.Pawn) & ~Bitboard(color, PieceType.King)) == 0;

    public string ToFen() => Fen.Export(this);

    public Position Clone() {
        var copy = new Position(ToFen());
        // Carry the hash history across so repetition claims survive the copy.
        copy._hashCount = 0;
        for (int i = 0; i < _hashCount; i++) copy.PushHash(_hashHistory[i]);
        return copy;
    }

    public override string ToString() {
        var text = new System.Text.StringBuilder();
        for (int rank = 7; rank >= 0; rank--) {
            text.Append(rank + 1).Append(' ');
            for (int file = 0; file < 8; file++) {
                Piece piece = _board[Squares.From(file, rank)];
                text.Append(piece == Piece.None ? '.' : piece.ToChar()).Append(' ');
            }
            text.Append('\n');
        }
        text.Append("  a b c d e f g h\n").Append(ToFen());
        return text.ToString();
    }

    private void PushUndo(UndoInfo undo) {
        if (_undoCount == _undoStack.Length) Array.Resize(ref _undoStack, _undoStack.Length * 2);
        _undoStack[_undoCount++] = undo;
    }

    private void PushHash(ulong key) {
        if (_hashCount == _hashHistory.Length) Array.Resize(ref _hashHistory, _hashHistory.Length * 2);
        _hashHistory[_hashCount++] = key;
    }

    private static CastlingRights[] BuildCastlingMask() {
        var mask = new CastlingRights[Squares.Count];
        Array.Fill(mask, CastlingRights.All);
        mask[Squares.E1] = ~CastlingRights.White;
        mask[Squares.A1] = ~CastlingRights.WhiteQueenside;
        mask[Squares.H1] = ~CastlingRights.WhiteKingside;
        mask[Squares.E8] = ~CastlingRights.Black;
        mask[Squares.A8] = ~CastlingRights.BlackQueenside;
        mask[Squares.H8] = ~CastlingRights.BlackKingside;
        return mask;
    }

    private struct UndoInfo {
        public Piece Captured;
        public CastlingRights Castling;
        public int EnPassantSquare;
        public int HalfmoveClock;
        public Move Move;
    }
}
