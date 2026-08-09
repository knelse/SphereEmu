namespace SphServer.Helpers.Networking;

/// <summary>
///     What the client is telling us it did. Derived from the record's own fields, not from how many
///     bytes the frame happens to occupy.
/// </summary>
public enum GameplayAction
{
    Unknown = 0,
    PositionUpdate,
    GroupActionOrPickup,
    ChatSend,

    /// <summary>
    ///     Shares chat's action code but carries no message. It must not be fed to the chat
    ///     assembler: arriving between a message's frames it would be taken for a continuation
    ///     and corrupt the message.
    /// </summary>
    ChatPeriodic,
    SelfOrUntargetedAction,
    Attack,
    Telemetry,

    /// <summary>A purchase from a vendor: which slot, how many, at what cost.</summary>
    Buy,

    /// <summary>The player interacting with a world object. <see cref="GameplayRecord.SubjectType" /> says which kind.</summary>
    ObjectInteract
}

/// <summary>
///     The header of a gameplay record, read as bits from the de-obfuscated body.
///     <code>
///     bit  0        position flag
///     bits 1..45    coarse position — PRESENT ONLY WHEN THE FLAG IS SET
///                     bits  1..16   trunc(x) + 32768
///                     bits 17..29   trunc(y) + 1200
///                     bits 30..45   trunc(z) + 32768
///     then          15 bits   client clock, ~24/s, wraps at 32768
///                   18 bits   entity id — the subject's id, bytes swapped vs the client id
///                   12 bits   subject ObjectType — 2 for the player's own records
///                    7 bits   tag: 1 telemetry, 5 object interaction, 12 position, 13 player action
///                   then a tag-dependent remainder; for tag 13 that is a 5-bit action code
///                   and a 1-bit flag, and for tag 12 a 4-bit sub-field then four 32-bit floats
///     </code>
///     Every field after the flag shifts by 45 bits when the position block is present, which is why
///     a fixed byte offset lands on the message type only when it is absent. Bit offsets are relative
///     to <see cref="ClientFrame.BodyOffset" />.
///     Where the widths come from, and what the captures can and cannot settle: docs/receive-path.md.
/// </summary>
public readonly struct GameplayRecord
{
    private const int BodyBit = ClientFrame.BodyOffset * 8;

    /// <summary>
    ///     Width of the block that appears between the 15-bit field and the entity id when the
    ///     position flag is set. Every later field shifts by this much.
    /// </summary>
    public const int PositionBlockWidth = 45;

    // A coarse integer position, 16 + 13 + 16 = 45 bits with none unaccounted for. Truncated
    // toward zero rather than rounded. y is narrower and biased differently because it is height
    // and stays in a small range. Offsets are absolute within the body, not relative to the block.
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

    /// <summary>Interaction with a world object; the subject type says which object.</summary>
    public const int TagObjectInteract = 5;

    // Subject types seen so far. These are ObjectType values, kept here as literals so the wire
    // layer stays free of game-data dependencies and can be replayed on its own.
    public const int SubjectTypePlayer = 2;        // ObjectType.Other
    public const int SubjectTypeNpcQuestTitle = 205;
    public const int SubjectTypeNpcTrade = 213;
    public const int SubjectTypeSackMobLoot = 407;

    /// <summary>
    ///     Absolute bit 172, where AttackFrame reads what it calls the
    ///     target id. On an attack that is the target; on an interaction frame the same offset holds
    ///     the player's own record, which is why those get reported as self-targeted. Exposed so the
    ///     harness can compare against the handler's reading — no server code reads it from here.
    /// </summary>
    public const int TargetIdBit = 172;

    // The identity block, as absolute bit offsets into the frame. Published because handlers need to
    // know how far into the frame the fields they use reach, in order to refuse a frame that is too
    // short for them — and a handler that restates these numbers locally is a second copy that can
    // drift away from the one that is read.
    public const int PositionFlagBit = BodyBit;
    public const int EntityIdBit = BodyBit + 16;
    public const int EntityIdWidth = 18;

    private readonly byte[] body;

    public GameplayRecord (byte[] decodedFrame)
    {
        body = decodedFrame;
        var identity = ReadIdentity(decodedFrame);
        PositionFlag = identity.PositionFlag;
        ClientClock = identity.ClientClock;
        EntityId = identity.EntityId;
        SubjectType = identity.SubjectType;
        Tag = identity.Tag;
        ActionCode = identity.ActionCode;
        ActionFlag = identity.ActionFlag;
    }

    /// <summary>
    ///     The header fields, without needing the frame as an array. The classifier reads a span and
    ///     handlers read a byte[]; both come through here so the bit offsets exist in one place only.
    /// </summary>
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
        var positionFlag = ReadBits(decodedFrame, PositionFlagBit, 1) == 1;

        // The block sits directly after the flag and only when the flag is set, so everything
        // after it — the clock included — moves along by that much.
        var shift = positionFlag ? PositionBlockWidth : 0;

        return new Identity(
            positionFlag,
            (ushort) ReadBits(decodedFrame, BodyBit + 1 + shift, 15),
            (uint) ReadBits(decodedFrame, EntityIdBit + shift, EntityIdWidth),
            (ushort) ReadBits(decodedFrame, BodyBit + 34 + shift, 12),
            (byte) ReadBits(decodedFrame, BodyBit + 46 + shift, 7),
            // What follows the tag depends on the tag: a position record has a 4-bit
            // sub-field where an action record has an action code and a flag.
            positionFlag ? (byte) 0 : (byte) ReadBits(decodedFrame, BodyBit + 53, 5),
            !positionFlag && ReadBits(decodedFrame, BodyBit + 58, 1) == 1);
    }

    public bool PositionFlag { get; }

    /// <summary>
    ///     Client clock, about 24 ticks per second, wrapping at 32768. In every gameplay frame, and
    ///     ticking at the same rate whether or not the position block is present — which is what
    ///     showed it to be one field read at two offsets.
    /// </summary>
    public ushort ClientClock { get; }
    public uint EntityId { get; }
    /// <summary>
    ///     The ObjectType of the entity this record is about — not a constant, as it first appeared.
    ///     It reads 2 (ObjectType.Other) on the player's own records, which is nearly all traffic, and
    ///     the target's own type when the player interacts with something: 213 NpcTrade, 407
    ///     SackMobLoot, 205 NpcQuestTitle, each matching a real ObjectType and each carrying that
    ///     object's id in <see cref="EntityId" /> (14 of 14 captured frames).
    ///     This is also what the old dispatch's "08 40 XX" / "5C 46 E1" signatures were really reading:
    ///     the subject type and tag seen as bytes, which is why a different target type produced a
    ///     different byte pattern.
    /// </summary>
    public ushort SubjectType { get; }
    public byte Tag { get; }
    public byte ActionCode { get; }
    public bool ActionFlag { get; }

    /// <summary>
    ///     Whether the tag is one this layout knows. Nothing calls this today — it is a description,
    ///     not a gate, and should not be mistaken for one. A frame passing it is not thereby safe to
    ///     hand to a handler: see the length checks each route needs.
    /// </summary>
    public bool LooksValid => Tag is TagTelemetry or TagPlayerAction or TagPosition or TagObjectInteract;

    /// <summary>Coarse x carried alongside the float coordinates. Position records only.</summary>
    public int CoarseX => (int) ReadBits(body, BodyBit + BlockXBit, BlockXWidth) - BlockXBias;

    /// <summary>Coarse y carried alongside the float coordinates. Position records only.</summary>
    public int CoarseY => (int) ReadBits(body, BodyBit + BlockYBit, BlockYWidth) - BlockYBias;

    /// <summary>Coarse z carried alongside the float coordinates. Position records only.</summary>
    public int CoarseZ => (int) ReadBits(body, BodyBit + BlockZBit, BlockZWidth) - BlockZBias;

    /// <summary>
    ///     Whether the coarse coordinates agree with the floats decoded from the same frame.
    ///     They are two encodings of one position, so disagreement means the frame was not read the
    ///     way the client wrote it. Truncation is toward zero, matching what the client does.
    /// </summary>
    public bool CoarsePositionAgrees (double x, double y, double z)
    {
        return CoarseX == (int) Math.Truncate(x)
               && CoarseY == (int) Math.Truncate(y)
               && CoarseZ == (int) Math.Truncate(z);
    }

    /// <summary>The target of an attack, as DamageTargetHandler reads it.</summary>
    public ushort TargetId => (ushort) ReadBits(body, TargetIdBit, 16);

    public GameplayAction Action => ActionOf(PositionFlag, Tag, ActionCode, ActionFlag);

    public static GameplayAction ActionOf (in Identity identity)
    {
        return ActionOf(identity.PositionFlag, identity.Tag, identity.ActionCode, identity.ActionFlag);
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

        // The flag is part of the action's identity, not a modifier: action 2 is a chat
        // message with it clear and a periodic frame with it set; action 5 is an attack with
        // it clear and an NPC interaction with it set.
        return (actionCode, actionFlag) switch
        {
            (1, _) => GameplayAction.GroupActionOrPickup,
            (2, false) => GameplayAction.ChatSend,
            (2, true) => GameplayAction.ChatPeriodic,
            // Action 3 is not named: it arrives at several lengths that are different messages,
            // and which is which is not established.
            (4, _) => GameplayAction.SelfOrUntargetedAction,
            (5, false) => GameplayAction.Attack,
            // Action 5 with the flag set is an NPC interaction, but it arrives at several
            // lengths and NpcInteractionHandler reads about 47 bytes, so naming it here would
            // point the short ones at a handler that runs off the end. The byte signature
            // carries the length requirement.
            (8, false) => GameplayAction.Buy,
            _ => GameplayAction.Unknown
        };
    }

    /// <summary>The id is stored with its bytes the other way round from the connection's client id.</summary>
    public bool BelongsTo (ushort clientId)
    {
        var swapped = (uint) (((clientId & 0xFF) << 8) | ((clientId >> 8) & 0xFF));
        return EntityId == swapped;
    }

    /// <summary>LSB-first: bit 0 is the least significant bit of byte 0.</summary>
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
