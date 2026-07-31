namespace Engine.Chess.Core;

/// <summary>
/// How a move interacts with the board. The numeric layout is deliberate:
/// bit 2 marks a capture and bit 3 marks a promotion, so both can be tested
/// with a single mask rather than a switch.
/// </summary>
public enum MoveFlag : byte {
    Quiet = 0,
    DoublePawnPush = 1,
    KingsideCastle = 2,
    QueensideCastle = 3,
    Capture = 4,
    EnPassant = 5,
    PromotionKnight = 8,
    PromotionBishop = 9,
    PromotionRook = 10,
    PromotionQueen = 11,
    CapturePromotionKnight = 12,
    CapturePromotionBishop = 13,
    CapturePromotionRook = 14,
    CapturePromotionQueen = 15,
}

/// <summary>
/// A move packed into 16 bits: origin square (6), destination square (6) and a
/// <see cref="MoveFlag"/> (4). Small enough to sit in move lists and the
/// transposition table without pointer chasing.
/// </summary>
public readonly struct Move : IEquatable<Move> {
    private const int ToShift = 6;
    private const int FlagShift = 12;
    private const int SquareMask = 0x3F;
    private const int CaptureBit = 4;
    private const int PromotionBit = 8;

    private readonly ushort _data;

    public Move(int from, int to, MoveFlag flag = MoveFlag.Quiet) {
        _data = (ushort)(from | (to << ToShift) | ((int)flag << FlagShift));
    }

    private Move(ushort data) => _data = data;

    /// <summary>The sentinel returned when no move exists (for example in a mated position).</summary>
    public static Move None => new((ushort)0);

    public int From => _data & SquareMask;

    public int To => (_data >> ToShift) & SquareMask;

    public MoveFlag Flag => (MoveFlag)(_data >> FlagShift);

    public bool IsNull => _data == 0;

    public bool IsCapture => ((int)Flag & CaptureBit) != 0;

    public bool IsPromotion => ((int)Flag & PromotionBit) != 0;

    public bool IsEnPassant => Flag == MoveFlag.EnPassant;

    public bool IsCastle => Flag is MoveFlag.KingsideCastle or MoveFlag.QueensideCastle;

    /// <summary>The piece a promoting pawn becomes, or <see cref="PieceType.None"/> for other moves.</summary>
    public PieceType PromotionPiece => IsPromotion
        ? (PieceType)(((int)Flag & 3) + (int)PieceType.Knight)
        : PieceType.None;

    /// <summary>The raw 16-bit encoding, for compact storage such as transposition entries.</summary>
    public ushort Encoded => _data;

    public static Move FromEncoded(ushort data) => new(data);

    public static MoveFlag PromotionFlag(PieceType promotion, bool isCapture) {
        int offset = (int)promotion - (int)PieceType.Knight;
        return (MoveFlag)((isCapture ? (int)MoveFlag.CapturePromotionKnight : (int)MoveFlag.PromotionKnight) + offset);
    }

    /// <summary>Long algebraic notation as used by UCI, for example <c>e2e4</c> or <c>e7e8q</c>.</summary>
    public string ToUci() {
        if (IsNull) return "0000";
        string promotion = IsPromotion
            ? char.ToLowerInvariant(Pieces.Create(Color.White, PromotionPiece).ToChar()).ToString()
            : string.Empty;
        return Squares.ToName(From) + Squares.ToName(To) + promotion;
    }

    public bool Equals(Move other) => _data == other._data;

    public override bool Equals(object? obj) => obj is Move other && Equals(other);

    public override int GetHashCode() => _data;

    public override string ToString() => ToUci();

    public static bool operator ==(Move left, Move right) => left._data == right._data;

    public static bool operator !=(Move left, Move right) => left._data != right._data;
}
