using Engine.Chess.Core;

namespace Engine.Chess.Board;

/// <summary>
/// Generates fully legal moves in one pass. Rather than producing pseudo-legal
/// moves and filtering them with make/unmake, it works out up front which squares
/// can resolve a check and which pieces are pinned, then only emits moves that
/// respect both. En passant is the one case that still needs an explicit test,
/// because it is the only move that removes two pieces from a rank at once.
/// </summary>
public static class MoveGenerator {
    public static void Generate(Position position, ref MoveList moves) =>
        Generate(position, ref moves, capturesOnly: false);

    /// <summary>
    /// Captures, en passant and queen promotions only. This is the quiescence
    /// search's move set, so it deliberately omits quiet moves and under-promotions.
    /// </summary>
    public static void GenerateCaptures(Position position, ref MoveList moves) =>
        Generate(position, ref moves, capturesOnly: true);

    private static void Generate(Position position, ref MoveList moves, bool capturesOnly) {
        Color us = position.SideToMove;
        Color them = us.Opponent();
        ulong occupied = position.Occupied;
        ulong ourPieces = position.Bitboard(us);
        ulong theirPieces = position.Bitboard(them);
        int king = position.KingSquare(us);

        ulong checkers = position.AttackersTo(king, them, occupied);
        int checkCount = Bitboards.PopCount(checkers);

        ulong destinations = capturesOnly ? theirPieces : ~ourPieces;
        GenerateKingMoves(position, ref moves, king, them, occupied, destinations);

        // In double check only the king can move: no single capture or block
        // can address two attackers at once.
        if (checkCount > 1) return;

        // Squares a non-king move must land on to be legal: unrestricted when not
        // in check, otherwise capture the checker or interpose on its line.
        ulong checkMask = Bitboards.Full;
        if (checkCount == 1) {
            int checker = Bitboards.LeastSignificant(checkers);
            checkMask = Attacks.Between(king, checker) | checkers;
        }

        ulong targets = checkMask & (capturesOnly ? theirPieces : ~ourPieces);
        ulong pinned = FindPinnedPieces(position, us, king, occupied, ourPieces);

        GeneratePawnMoves(position, ref moves, us, king, occupied, theirPieces, pinned, checkMask, capturesOnly);
        GeneratePieceMoves(position, ref moves, us, PieceType.Knight, king, occupied, pinned, targets);
        GeneratePieceMoves(position, ref moves, us, PieceType.Bishop, king, occupied, pinned, targets);
        GeneratePieceMoves(position, ref moves, us, PieceType.Rook, king, occupied, pinned, targets);
        GeneratePieceMoves(position, ref moves, us, PieceType.Queen, king, occupied, pinned, targets);

        if (!capturesOnly && checkCount == 0) {
            GenerateCastles(position, ref moves, us, them, occupied);
        }
    }

    /// <summary>
    /// Our pieces that stand alone between our king and an enemy slider. Such a
    /// piece may still move, but only along the line joining king and pinner.
    /// </summary>
    private static ulong FindPinnedPieces(Position position, Color us, int king, ulong occupied, ulong ourPieces) {
        Color them = us.Opponent();
        ulong theirQueens = position.Bitboard(them, PieceType.Queen);

        // Sliders that would hit the king on an empty board are the only pin candidates.
        ulong snipers =
            (Attacks.Rook(king, 0) & (position.Bitboard(them, PieceType.Rook) | theirQueens)) |
            (Attacks.Bishop(king, 0) & (position.Bitboard(them, PieceType.Bishop) | theirQueens));

        ulong pinned = 0UL;
        while (snipers != 0) {
            int sniper = Bitboards.PopLeastSignificant(ref snipers);
            ulong blockers = Attacks.Between(king, sniper) & occupied;
            if (Bitboards.PopCount(blockers) == 1) pinned |= blockers & ourPieces;
        }
        return pinned;
    }

    private static void GenerateKingMoves(
        Position position, ref MoveList moves, int king, Color them, ulong occupied, ulong destinations) {
        // The king must not step along the line it is being checked on, so it is
        // removed from the occupancy before asking whether a square is attacked.
        ulong withoutKing = occupied ^ Bitboards.Square(king);
        ulong candidates = Attacks.King(king) & destinations;

        while (candidates != 0) {
            int to = Bitboards.PopLeastSignificant(ref candidates);
            if (position.AttackersTo(to, them, withoutKing) != 0) continue;
            moves.Add(new Move(king, to, Bitboards.Contains(occupied, to) ? MoveFlag.Capture : MoveFlag.Quiet));
        }
    }

    private static void GeneratePieceMoves(
        Position position, ref MoveList moves, Color us, PieceType type,
        int king, ulong occupied, ulong pinned, ulong targets) {
        ulong pieces = position.Bitboard(us, type);

        while (pieces != 0) {
            int from = Bitboards.PopLeastSignificant(ref pieces);
            ulong allowed = targets;
            if (Bitboards.Contains(pinned, from)) allowed &= Attacks.Line(king, from);

            ulong candidates = Attacks.Of(type, from, occupied) & allowed;
            while (candidates != 0) {
                int to = Bitboards.PopLeastSignificant(ref candidates);
                moves.Add(new Move(from, to, Bitboards.Contains(occupied, to) ? MoveFlag.Capture : MoveFlag.Quiet));
            }
        }
    }

    private static void GeneratePawnMoves(
        Position position, ref MoveList moves, Color us, int king,
        ulong occupied, ulong theirPieces, ulong pinned, ulong checkMask, bool capturesOnly) {
        ulong pawns = position.Bitboard(us, PieceType.Pawn);
        if (pawns == 0) return;

        bool white = us == Color.White;
        ulong empty = ~occupied;
        ulong promotionRank = white ? Bitboards.Rank8 : Bitboards.Rank1;
        ulong doublePushRank = white ? Bitboards.Rank3 : Bitboards.Rank6;

        int pushDelta = white ? 8 : -8;
        int leftDelta = white ? 7 : -9;   // towards the a-file
        int rightDelta = white ? 9 : -7;  // towards the h-file

        ulong singlePushes = (white ? Bitboards.North(pawns) : Bitboards.South(pawns)) & empty;
        ulong doublePushes =
            (white ? Bitboards.North(singlePushes & doublePushRank) : Bitboards.South(singlePushes & doublePushRank))
            & empty & checkMask;

        // A pawn one step from promoting must push even in a captures-only search,
        // because turning into a queen changes the material balance.
        ulong quietPromotions = singlePushes & promotionRank & checkMask;
        ulong quietPushes = singlePushes & ~promotionRank & checkMask;

        if (!capturesOnly) {
            AddPawnMoves(ref moves, quietPushes, pushDelta, MoveFlag.Quiet, king, pinned);
            AddPawnMoves(ref moves, doublePushes, pushDelta * 2, MoveFlag.DoublePawnPush, king, pinned);
            AddPromotions(ref moves, quietPromotions, pushDelta, isCapture: false, king, pinned, allPromotions: true);
        } else {
            AddPromotions(ref moves, quietPromotions, pushDelta, isCapture: false, king, pinned, allPromotions: false);
        }

        ulong captureTargets = theirPieces & checkMask;
        ulong leftCaptures = (white ? Bitboards.NorthWest(pawns) : Bitboards.SouthWest(pawns)) & captureTargets;
        ulong rightCaptures = (white ? Bitboards.NorthEast(pawns) : Bitboards.SouthEast(pawns)) & captureTargets;

        AddPawnMoves(ref moves, leftCaptures & ~promotionRank, leftDelta, MoveFlag.Capture, king, pinned);
        AddPawnMoves(ref moves, rightCaptures & ~promotionRank, rightDelta, MoveFlag.Capture, king, pinned);
        AddPromotions(ref moves, leftCaptures & promotionRank, leftDelta, true, king, pinned, !capturesOnly);
        AddPromotions(ref moves, rightCaptures & promotionRank, rightDelta, true, king, pinned, !capturesOnly);

        if (position.EnPassantSquare != Squares.None) {
            GenerateEnPassant(position, ref moves, us, king, occupied);
        }
    }

    private static void AddPawnMoves(
        ref MoveList moves, ulong destinations, int delta, MoveFlag flag, int king, ulong pinned) {
        while (destinations != 0) {
            int to = Bitboards.PopLeastSignificant(ref destinations);
            int from = to - delta;
            if (IsPinnedOffLine(from, to, king, pinned)) continue;
            moves.Add(new Move(from, to, flag));
        }
    }

    private static void AddPromotions(
        ref MoveList moves, ulong destinations, int delta, bool isCapture,
        int king, ulong pinned, bool allPromotions) {
        while (destinations != 0) {
            int to = Bitboards.PopLeastSignificant(ref destinations);
            int from = to - delta;
            if (IsPinnedOffLine(from, to, king, pinned)) continue;

            moves.Add(new Move(from, to, Move.PromotionFlag(PieceType.Queen, isCapture)));
            if (!allPromotions) continue;

            moves.Add(new Move(from, to, Move.PromotionFlag(PieceType.Rook, isCapture)));
            moves.Add(new Move(from, to, Move.PromotionFlag(PieceType.Bishop, isCapture)));
            moves.Add(new Move(from, to, Move.PromotionFlag(PieceType.Knight, isCapture)));
        }
    }

    private static bool IsPinnedOffLine(int from, int to, int king, ulong pinned) =>
        Bitboards.Contains(pinned, from) && !Bitboards.Contains(Attacks.Line(king, from), to);

    /// <summary>
    /// En passant is validated by rebuilding the occupancy it would produce and
    /// re-testing the king. Nothing cheaper is safe: the capture vacates two squares
    /// on the same rank, which can uncover a rook or queen that no pin test saw, and
    /// it can also be the legal reply to a check from the pawn being captured.
    /// </summary>
    private static void GenerateEnPassant(
        Position position, ref MoveList moves, Color us, int king, ulong occupied) {
        Color them = us.Opponent();
        int to = position.EnPassantSquare;
        int capturedSquare = us == Color.White ? to - 8 : to + 8;

        // A pawn that could capture onto the en-passant square attacks it from the
        // opposite side, so the opponent's attack pattern finds the candidates.
        ulong candidates = Attacks.Pawn(to, them) & position.Bitboard(us, PieceType.Pawn);

        while (candidates != 0) {
            int from = Bitboards.PopLeastSignificant(ref candidates);
            ulong afterCapture = (occupied ^ Bitboards.Square(from) ^ Bitboards.Square(capturedSquare))
                                 | Bitboards.Square(to);

            ulong theirQueens = position.Bitboard(them, PieceType.Queen);
            bool exposesKing =
                (Attacks.Rook(king, afterCapture) & (position.Bitboard(them, PieceType.Rook) | theirQueens)) != 0 ||
                (Attacks.Bishop(king, afterCapture) & (position.Bitboard(them, PieceType.Bishop) | theirQueens)) != 0 ||
                (Attacks.Knight(king) & position.Bitboard(them, PieceType.Knight)) != 0 ||
                (Attacks.King(king) & position.Bitboard(them, PieceType.King)) != 0 ||
                (Attacks.Pawn(king, us) & position.Bitboard(them, PieceType.Pawn)
                                        & ~Bitboards.Square(capturedSquare)) != 0;

            if (!exposesKing) moves.Add(new Move(from, to, MoveFlag.EnPassant));
        }
    }

    private static void GenerateCastles(
        Position position, ref MoveList moves, Color us, Color them, ulong occupied) {
        int king = position.KingSquare(us);
        ulong rooks = position.Bitboard(us, PieceType.Rook);

        if (us == Color.White) {
            if (position.Castling.HasFlag(CastlingRights.WhiteKingside)) {
                TryAddCastle(position, ref moves, them, occupied, rooks, king,
                    rookSquare: Squares.H1, emptySquares: [Squares.F1, Squares.G1],
                    safeSquares: [Squares.E1, Squares.F1, Squares.G1],
                    to: Squares.G1, MoveFlag.KingsideCastle);
            }
            if (position.Castling.HasFlag(CastlingRights.WhiteQueenside)) {
                TryAddCastle(position, ref moves, them, occupied, rooks, king,
                    rookSquare: Squares.A1, emptySquares: [Squares.B1, Squares.C1, Squares.D1],
                    safeSquares: [Squares.E1, Squares.D1, Squares.C1],
                    to: Squares.C1, MoveFlag.QueensideCastle);
            }
        } else {
            if (position.Castling.HasFlag(CastlingRights.BlackKingside)) {
                TryAddCastle(position, ref moves, them, occupied, rooks, king,
                    rookSquare: Squares.H8, emptySquares: [Squares.F8, Squares.G8],
                    safeSquares: [Squares.E8, Squares.F8, Squares.G8],
                    to: Squares.G8, MoveFlag.KingsideCastle);
            }
            if (position.Castling.HasFlag(CastlingRights.BlackQueenside)) {
                TryAddCastle(position, ref moves, them, occupied, rooks, king,
                    rookSquare: Squares.A8, emptySquares: [Squares.B8, Squares.C8, Squares.D8],
                    safeSquares: [Squares.E8, Squares.D8, Squares.C8],
                    to: Squares.C8, MoveFlag.QueensideCastle);
            }
        }
    }

    private static void TryAddCastle(
        Position position, ref MoveList moves, Color them, ulong occupied, ulong rooks, int king,
        int rookSquare, ReadOnlySpan<int> emptySquares, ReadOnlySpan<int> safeSquares, int to, MoveFlag flag) {
        // A hand-written FEN can claim rights without the matching rook.
        if (!Bitboards.Contains(rooks, rookSquare)) return;

        foreach (int square in emptySquares) {
            if (Bitboards.Contains(occupied, square)) return;
        }
        // The king may not start in, pass through, or land on an attacked square.
        // The b-file square of a queenside castle is exempt: only the rook crosses it.
        foreach (int square in safeSquares) {
            if (position.AttackersTo(square, them, occupied) != 0) return;
        }

        moves.Add(new Move(king, to, flag));
    }
}
