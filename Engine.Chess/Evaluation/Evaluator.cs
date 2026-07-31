using Engine.Chess.Board;
using Engine.Chess.Core;

namespace Engine.Chess.Evaluation;

/// <summary>
/// Static evaluation in centipawns, always from the side to move's point of view
/// so the search can negate it at every ply.
/// </summary>
/// <remarks>
/// Every term is scored twice, once for a middlegame board and once for an endgame
/// board, and the two are blended by how much material is left. Without that taper
/// an engine plays the opening like an endgame: it marches its king up the board
/// while queens are still on.
/// </remarks>
public static class Evaluator {
    /// <summary>Material values in centipawns, indexed by <see cref="PieceType"/>.</summary>
    public static readonly int[] MidgameValue = [0, 90, 330, 350, 490, 960, 0];

    public static readonly int[] EndgameValue = [0, 110, 300, 320, 540, 980, 0];

    /// <summary>Simple values used for move ordering and material counting in the UI.</summary>
    public static readonly int[] PieceValue = [0, 100, 320, 330, 500, 900, 0];

    /// <summary>How much each piece contributes to "the middlegame is still on". Sums to 24.</summary>
    private static readonly int[] PhaseWeight = [0, 0, 1, 1, 2, 4, 0];

    private const int TotalPhase = 24;

    private const int BishopPairMidgame = 30;
    private const int BishopPairEndgame = 46;
    private const int DoubledPawnMidgame = -10;
    private const int DoubledPawnEndgame = -22;
    private const int IsolatedPawnMidgame = -14;
    private const int IsolatedPawnEndgame = -18;
    private const int RookOpenFileMidgame = 22;
    private const int RookSemiOpenFileMidgame = 11;
    private const int KingShieldPawn = 12;
    private const int Tempo = 12;

    /// <summary>Bonus for a passed pawn by how many ranks it has advanced from home.</summary>
    private static readonly int[] PassedPawnMidgame = [0, 5, 10, 20, 35, 60, 100, 0];

    private static readonly int[] PassedPawnEndgame = [0, 12, 22, 38, 65, 110, 170, 0];

    /// <summary>Centipawns per attacked square, indexed by <see cref="PieceType"/>.</summary>
    private static readonly int[] MobilityMidgame = [0, 0, 4, 5, 3, 2, 0];

    private static readonly int[] MobilityEndgame = [0, 0, 4, 5, 4, 4, 0];

    /// <summary>Penalty applied to the defender, scaled by how many pieces attack the king zone.</summary>
    private static readonly int[] KingAttackWeight = [0, 4, 12, 12, 18, 30, 0];

    private static readonly int[] KingAttackScale = [0, 0, 50, 75, 88, 94, 97, 99, 100, 100, 100, 100, 100, 100, 100, 100];

    /// <summary>Squares ahead of a pawn on its own and adjacent files: empty of enemy pawns means passed.</summary>
    private static readonly ulong[,] PassedPawnMask = BuildPassedPawnMasks();

    /// <summary>Squares ahead of a pawn on its own file, for doubled-pawn detection.</summary>
    private static readonly ulong[,] ForwardFileMask = BuildForwardFileMasks();

    /// <summary>The king's square plus its immediate surroundings, used for king-safety pressure.</summary>
    private static readonly ulong[] KingZone = BuildKingZones();

    public static int Evaluate(Position position) {
        int phase = Phase(position);

        int whiteMidgame = ScoreSide(position, Color.White, out int whiteEndgame);
        int blackMidgame = ScoreSide(position, Color.Black, out int blackEndgame);

        int midgame = whiteMidgame - blackMidgame;
        int endgame = whiteEndgame - blackEndgame;

        int blended = ((midgame * phase) + (endgame * (TotalPhase - phase))) / TotalPhase;
        int score = position.SideToMove == Color.White ? blended : -blended;
        return score + Tempo;
    }

    /// <summary>
    /// How far through the game the position is: 24 with all pieces on, 0 once only
    /// pawns and kings remain. Promotions can push the raw sum above 24, so it is clamped.
    /// </summary>
    public static int Phase(Position position) {
        int phase = 0;
        for (PieceType type = PieceType.Knight; type <= PieceType.Queen; type++) {
            phase += Bitboards.PopCount(position.Bitboard(type)) * PhaseWeight[(int)type];
        }
        return Math.Min(phase, TotalPhase);
    }

    /// <summary>Raw material difference in centipawns from white's point of view. Drives the UI capture strip.</summary>
    public static int MaterialBalance(Position position) {
        int balance = 0;
        for (PieceType type = PieceType.Pawn; type <= PieceType.Queen; type++) {
            int count = Bitboards.PopCount(position.Bitboard(Color.White, type))
                      - Bitboards.PopCount(position.Bitboard(Color.Black, type));
            balance += count * PieceValue[(int)type];
        }
        return balance;
    }

    private static int ScoreSide(Position position, Color us, out int endgame) {
        Color them = us.Opponent();
        ulong occupied = position.Occupied;
        ulong ourPawns = position.Bitboard(us, PieceType.Pawn);
        ulong theirPawns = position.Bitboard(them, PieceType.Pawn);
        ulong ourPieces = position.Bitboard(us);

        // Squares the enemy pawns cover are not real mobility, so they are excluded.
        ulong theirPawnAttacks = Bitboards.PawnAttacks(theirPawns, them);
        ulong mobilityArea = ~(ourPieces | theirPawnAttacks);

        int enemyKingZone = position.KingSquare(them);
        ulong kingZone = KingZone[enemyKingZone];
        int kingAttackers = 0;
        int kingAttackScore = 0;

        int midgame = 0;
        endgame = 0;

        for (PieceType type = PieceType.Pawn; type <= PieceType.King; type++) {
            ulong pieces = position.Bitboard(us, type);
            int[] midgameTable = PieceSquareTables.Midgame[(int)type];
            int[] endgameTable = PieceSquareTables.Endgame[(int)type];

            while (pieces != 0) {
                int square = Bitboards.PopLeastSignificant(ref pieces);
                int relative = us == Color.White ? square : Squares.Flip(square);

                midgame += MidgameValue[(int)type] + midgameTable[relative];
                endgame += EndgameValue[(int)type] + endgameTable[relative];

                if (type is PieceType.Pawn or PieceType.King) continue;

                ulong attacks = Attacks.Of(type, square, occupied);
                int mobility = Bitboards.PopCount(attacks & mobilityArea);
                midgame += mobility * MobilityMidgame[(int)type];
                endgame += mobility * MobilityEndgame[(int)type];

                ulong zonePressure = attacks & kingZone;
                if (zonePressure != 0) {
                    kingAttackers++;
                    kingAttackScore += Bitboards.PopCount(zonePressure) * KingAttackWeight[(int)type];
                }

                if (type == PieceType.Rook) {
                    ulong file = Bitboards.Files[Squares.FileOf(square)];
                    if ((file & ourPawns) == 0) {
                        midgame += (file & theirPawns) == 0 ? RookOpenFileMidgame : RookSemiOpenFileMidgame;
                    }
                }
            }
        }

        // A single attacker rarely amounts to an attack; the scale ramps up sharply
        // from two and saturates, so a swarm cannot produce an absurd score.
        midgame += kingAttackScore * KingAttackScale[Math.Min(kingAttackers, KingAttackScale.Length - 1)] / 100;

        if (Bitboards.PopCount(position.Bitboard(us, PieceType.Bishop)) >= 2) {
            midgame += BishopPairMidgame;
            endgame += BishopPairEndgame;
        }

        ScorePawnStructure(us, ourPawns, theirPawns, ref midgame, ref endgame);
        midgame += ScoreKingShield(position, us, ourPawns);

        return midgame;
    }

    private static void ScorePawnStructure(
        Color us, ulong ourPawns, ulong theirPawns, ref int midgame, ref int endgame) {
        ulong pawns = ourPawns;
        while (pawns != 0) {
            int square = Bitboards.PopLeastSignificant(ref pawns);
            int file = Squares.FileOf(square);

            if ((ForwardFileMask[(int)us, square] & ourPawns) != 0) {
                midgame += DoubledPawnMidgame;
                endgame += DoubledPawnEndgame;
            }

            if ((Bitboards.AdjacentFiles[file] & ~Bitboards.Files[file] & ourPawns) == 0) {
                midgame += IsolatedPawnMidgame;
                endgame += IsolatedPawnEndgame;
            }

            if ((PassedPawnMask[(int)us, square] & theirPawns) == 0) {
                int advanced = us == Color.White ? Squares.RankOf(square) : 7 - Squares.RankOf(square);
                midgame += PassedPawnMidgame[advanced];
                endgame += PassedPawnEndgame[advanced];
            }
        }
    }

    /// <summary>Rewards pawns still standing in front of the king, which is most of what king safety is.</summary>
    private static int ScoreKingShield(Position position, Color us, ulong ourPawns) {
        int king = position.KingSquare(us);
        ulong shield = Attacks.King(king) & (us == Color.White
            ? Bitboards.North(Bitboards.Square(king)) | Bitboards.NorthEast(Bitboards.Square(king))
              | Bitboards.NorthWest(Bitboards.Square(king))
            : Bitboards.South(Bitboards.Square(king)) | Bitboards.SouthEast(Bitboards.Square(king))
              | Bitboards.SouthWest(Bitboards.Square(king)));
        return Bitboards.PopCount(shield & ourPawns) * KingShieldPawn;
    }

    private static ulong[,] BuildPassedPawnMasks() {
        var masks = new ulong[2, Squares.Count];
        for (int square = 0; square < Squares.Count; square++) {
            ulong files = Bitboards.AdjacentFiles[Squares.FileOf(square)];
            ulong ahead = 0UL;
            ulong behind = 0UL;
            for (int rank = Squares.RankOf(square) + 1; rank < 8; rank++) ahead |= Bitboards.Ranks[rank];
            for (int rank = Squares.RankOf(square) - 1; rank >= 0; rank--) behind |= Bitboards.Ranks[rank];
            masks[(int)Color.White, square] = files & ahead;
            masks[(int)Color.Black, square] = files & behind;
        }
        return masks;
    }

    private static ulong[,] BuildForwardFileMasks() {
        var masks = new ulong[2, Squares.Count];
        for (int square = 0; square < Squares.Count; square++) {
            ulong file = Bitboards.Files[Squares.FileOf(square)];
            ulong ahead = 0UL;
            ulong behind = 0UL;
            for (int rank = Squares.RankOf(square) + 1; rank < 8; rank++) ahead |= Bitboards.Ranks[rank];
            for (int rank = Squares.RankOf(square) - 1; rank >= 0; rank--) behind |= Bitboards.Ranks[rank];
            masks[(int)Color.White, square] = file & ahead;
            masks[(int)Color.Black, square] = file & behind;
        }
        return masks;
    }

    private static ulong[] BuildKingZones() {
        var zones = new ulong[Squares.Count];
        for (int square = 0; square < Squares.Count; square++) {
            zones[square] = Attacks.King(square) | Bitboards.Square(square);
        }
        return zones;
    }
}
