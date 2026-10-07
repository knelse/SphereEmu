namespace SphServer.Helpers.Networking;

/// Named from the record fields, independent of frame length
public enum GameplayAction
{
    Unknown = 0,
    PositionUpdate,
    GroupActionOrPickup,
    ChatSend,

    /// Same action code as chat and no message, so a chat assembler would take it for a
    /// continuation
    ChatPeriodic,
    SelfOrUntargetedAction,
    Attack,
    Telemetry,

    /// Vendor slot, count, and cost
    Buy,

    ObjectInteract
}

/// Bit 0 position flag; if set, x 1-16 +32768, y 17-29 +1200, z 30-45 +32768; then clock 15 (~24/s,
/// wrap 32768), entity id 18 (byte-swapped), subject type 12, tag 7
/// Tag 13 is a 5-bit action plus a flag; tag 12 is a 4-bit field then four f32; later fields shift
/// 45 when the flag is set.
public readonly struct GameplayRecord
{
    private const int BodyBit = ClientFrame.BodyOffset * 8;

    /// Inserted after the flag when it is set; every later field shifts by this
    public const int PositionBlockWidth = 45;

    // Coarse pos, truncated toward zero: x 16 bias 32768, y 13 bias 1200, z 16 bias 32768
    private const int BlockXBit = 1;
    private const int BlockXWidth = 16;
    private const int BlockXBias = 32768;
    private const int BlockYBit = 17;
    private const int BlockYWidth = 13;
    private const int BlockYBias = 1200;
    private const int BlockZBit = 30;
    private const int BlockZWidth = 16;
    private const int BlockZBias = 32768;
    public const int TagTelemetry = 1;
    public const int TagPosition = 12;
    public const int TagPlayerAction = 13;

    public const int TagObjectInteract = 5;

    // ObjectType values as literals so this layer does not reference game data
    public const int SubjectTypePlayer = 2;        // ObjectType.Other
    public const int SubjectTypeNpcQuestTitle = 205;
    public const int SubjectTypeNpcTrade = 213;
    public const int SubjectTypeSackMobLoot = 407;

    /// Attack target at bit 172; an interaction frame holds the player's own record here
    public const int TargetIdBit = 172;

    // Absolute bit offsets of the identity fields in the frame
    public const int PositionFlagBit = BodyBit;
    public const int EntityIdBit = BodyBit + 16;
    public const int EntityIdWidth = 18;

    private readonly byte[] body;

    public GameplayRecord (byte[] decodedFrame)
    {
        body = decodedFrame;
        var identity = ReadIdentity (decodedFrame);
        PositionFlag = identity.PositionFlag;
        ClientClock = identity.ClientClock;
        EntityId = identity.EntityId;
        SubjectType = identity.SubjectType;
        Tag = identity.Tag;
        ActionCode = identity.ActionCode;
        ActionFlag = identity.ActionFlag;
    }

    /// Header fields from a span, so the bit offsets live in one reader
    public readonly record struct Identity (
        bool PositionFlag,
        ushort ClientClock,
        uint EntityId,
        ushort SubjectType,
        byte Tag,
        byte ActionCode,
        bool ActionFlag);

    public static Identity ReadIdentity (ReadOnlySpan<byte> decodedFrame)
    {
        var positionFlag = ReadBits (decodedFrame, PositionFlagBit, 1) == 1;

        // Later fields, including the clock, shift by the block width when the flag is set
        var shift = positionFlag ? PositionBlockWidth : 0;

        return new Identity (
            positionFlag,
            (ushort) ReadBits (decodedFrame, BodyBit + 1 + shift, 15),
            (uint) ReadBits (decodedFrame, EntityIdBit + shift, EntityIdWidth),
            (ushort) ReadBits (decodedFrame, BodyBit + 34 + shift, 12),
            (byte) ReadBits (decodedFrame, BodyBit + 46 + shift, 7),
            // After the tag, a position record has a 4-bit sub-field and an action record has a
            // code plus a flag
            positionFlag ? (byte) 0 : (byte) ReadBits (decodedFrame, BodyBit + 53, 5),
            !positionFlag && ReadBits (decodedFrame, BodyBit + 58, 1) == 1);
    }

    public bool PositionFlag { get; }

    /// About 24 ticks per second, wraps at 32768, same rate with or without the position block
    public ushort ClientClock { get; }
    public uint EntityId { get; }
    /// ObjectType of the subject: 2 on the player's own records, otherwise the target type (213
    /// trade, 407 loot, 205 quest)
    public ushort SubjectType { get; }
    public byte Tag { get; }
    public byte ActionCode { get; }
    public bool ActionFlag { get; }

    public bool LooksValid => Tag is TagTelemetry or TagPlayerAction or TagPosition or TagObjectInteract;

    /// Integer twin of float x, position records only
    public int CoarseX => (int) ReadBits (body, BodyBit + BlockXBit, BlockXWidth) - BlockXBias;

    /// Integer twin of float y, position records only
    public int CoarseY => (int) ReadBits (body, BodyBit + BlockYBit, BlockYWidth) - BlockYBias;

    /// Integer twin of float z, position records only
    public int CoarseZ => (int) ReadBits (body, BodyBit + BlockZBit, BlockZWidth) - BlockZBias;

    /// Coarse values match the floats truncated toward zero
    public bool CoarsePositionAgrees (double x, double y, double z)
    {
        return CoarseX == (int) Math.Truncate (x)
               && CoarseY == (int) Math.Truncate (y)
               && CoarseZ == (int) Math.Truncate (z);
    }

    public ushort TargetId => (ushort) ReadBits (body, TargetIdBit, 16);

    public GameplayAction Action => ActionOf (PositionFlag, Tag, ActionCode, ActionFlag);

    public static GameplayAction ActionOf (in Identity identity)
    {
        return ActionOf (identity.PositionFlag, identity.Tag, identity.ActionCode, identity.ActionFlag);
    }

    public static GameplayAction ActionOf (bool positionFlag, byte tag, byte actionCode, bool actionFlag)
    {
        if (positionFlag)
        {
            return GameplayAction.PositionUpdate;
        }

        if (tag == TagTelemetry)
        {
            return GameplayAction.Telemetry;
        }

        if (tag == TagObjectInteract)
        {
            return GameplayAction.ObjectInteract;
        }

        if (tag != TagPlayerAction)
        {
            return GameplayAction.Unknown;
        }

        // The flag is part of the action: 2 clear is chat, 2 set is periodic; 5 clear is attack, 5
        // set is an NPC interaction
        return (actionCode, actionFlag) switch
        {
            (1, _) => GameplayAction.GroupActionOrPickup,
            (2, false) => GameplayAction.ChatSend,
            (2, true) => GameplayAction.ChatPeriodic,
            // Action 3 stays unnamed; several lengths are different messages
            (4, _) => GameplayAction.SelfOrUntargetedAction,
            (5, false) => GameplayAction.Attack,
            // Action 5 with the flag set is an NPC interaction whose length is not fixed here
            (8, false) => GameplayAction.Buy,
            _ => GameplayAction.Unknown
        };
    }

    /// Entity id is the client id with its bytes swapped
    public bool BelongsTo (ushort clientId)
    {
        var swapped = (uint) (((clientId & 0xFF) << 8) | ((clientId >> 8) & 0xFF));
        return EntityId == swapped;
    }

    /// Bit 0 is the least significant bit of byte 0
    private static ulong ReadBits (ReadOnlySpan<byte> data, int startBit, int width)
    {
        ulong value = 0;
        for (var i = 0; i < width; i++)
        {
            var bit = startBit + i;
            var index = bit >> 3;
            if (index >= data.Length)
            {
                break;
            }

            if ((data[index] & (1 << (bit & 7))) != 0)
            {
                value |= 1UL << i;
            }
        }

        return value;
    }
}
