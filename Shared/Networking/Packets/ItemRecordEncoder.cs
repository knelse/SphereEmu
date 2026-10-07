using System;
using SphereHelpers.Extensions;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.Mbc;
using static SphServer.Shared.BitStream.SphBitStream;

namespace SphServer.Packets;

/// <summary>
/// FULL_SPAWN for regular items; guild and abilities use tags 2000-2015 because classic types
/// 976/977/979/982 are not loaded modules; containerObjectId is ByteSwap(ClientIndex) for a player
/// </summary>
public static class ItemRecordEncoder
{
    private const int FullSpawn = 0x7C;

    /// <summary>
    /// x while inside a container is 1e6, not a world coordinate
    /// </summary>
    private const float ContainedX = 1000000.0f;

    /// <summary>
    /// Capture variants of this placement word are all ground drops
    /// </summary>
    private const uint PlacementState = 0x322C8A00;

    /// <summary>
    /// 7-bit marker, then type and length, then the payload
    /// </summary>
    private const uint FollowOnRecord = 5;

    private const uint PutHereMessage = 0;
    private const uint PropertiesMessage = 9;

    /// <summary>
    /// Ground container 0xFF00 is not a process id, so MBC guild skips SetParent
    /// </summary>
    private const ushort GroundContainerSentinel = 0xFF00;

    /// <summary>
    /// No-suffix sentinel, not an on-wire magnitude; locale ids are 0-N and include 17, so none is
    /// PackedNoSuffix
    /// </summary>
    public const int NoSuffix = -1;

    /// <summary>
    /// On-wire none: __hasSuffix 1, suffix_length 0, suffix 2, six bits
    /// </summary>
    private const int PackedNoSuffix = 17;

    public static byte[] Encode (ItemDbEntry item, ushort containerObjectId,
        float x = ContainedX, float y = 0f, float z = 0f)
    {
        return Encode ((ushort) item.Id, (int) item.WireObjectType, item.GameId, SuffixWireFor (item),
            containerObjectId, x, y, z, item.CurrentDurability, item.Durability, item.GameObjectType);
    }

    /// <summary>
    /// No game object id: object type alone, so every item of a type shares one icon
    /// </summary>
    public static byte[] EncodeWithoutGameId (ushort entityId, int objectType, ushort containerObjectId,
        float x = ContainedX, float y = 0f, float z = 0f)
    {
        var stream = GetWriteBitStream ();
        WriteItemHeader (stream, entityId, objectType);
        WriteUInt32Full (stream, BitConverter.SingleToUInt32Bits (x));
        WriteUInt32Full (stream, BitConverter.SingleToUInt32Bits (y));
        WriteUInt32Full (stream, BitConverter.SingleToUInt32Bits (z));
        stream.WriteByte (0, 8);
        stream.WriteUInt32 (0x322C89, 24);
        stream.WriteByte (0, 1);
        WriteUInt32Full (stream, 0x05090A89);
        stream.WriteUInt16 (containerObjectId, 16);
        stream.WriteUInt32 (0x7FFFFF, 23);
        return Packet.ToByteArray (stream.GetStreamData (), 3);
    }

    public static byte[] Encode (ushort entityId, int objectType, int gameObjectId, int suffix,
        ushort containerObjectId, float x = ContainedX, float y = 0f, float z = 0f,
        int currentDurability = 0, int maxDurability = 0,
        GameObjectType gameObjectType = GameObjectType.Unknown)
    {
        if (IsGuildFamily ((ObjectType) objectType))
        {
            return EncodeLiveGuild (entityId, objectType, gameObjectId, suffix, containerObjectId,
                x, y, z, currentDurability, maxDurability, gameObjectType);
        }

        return EncodeClassic (entityId, objectType, gameObjectId, suffix, containerObjectId, x, y, z);
    }

    private static byte[] EncodeClassic (ushort entityId, int objectType, int gameObjectId, int suffix,
        ushort containerObjectId, float x, float y, float z)
    {
        var stream = GetWriteBitStream ();
        WriteItemHeader (stream, entityId, objectType);
        WriteUInt32Full (stream, BitConverter.SingleToUInt32Bits (x));
        WriteUInt32Full (stream, BitConverter.SingleToUInt32Bits (y));
        WriteUInt32Full (stream, BitConverter.SingleToUInt32Bits (z));
        WriteUInt32Full (stream, PlacementState);
        stream.WriteByte (1, 1);

        // Masked to the field width: a value past it lengthens the record and shifts everything
        // after it
        stream.WriteUInt16 ((ushort) (gameObjectId & 0x3FFF), 14);
        WriteSuffix (stream, suffix);

        // "You are inside this container": the only field that gives an item a parent
        stream.WriteByte ((byte) FollowOnRecord, 7);
        stream.WriteByte ((byte) PutHereMessage, 8);
        stream.WriteByte (3, 8);
        stream.WriteUInt32 (containerObjectId, 24);

        // Property 0 stays -1: any other value that is not the player's id is a second refusal,
        // apart from the parent check
        stream.WriteByte ((byte) FollowOnRecord, 7);
        stream.WriteByte ((byte) PropertiesMessage, 8);
        stream.WriteByte (5, 8);
        stream.WriteByte (0, 8);
        WriteUInt32Full (stream, uint.MaxValue);

        stream.WriteByte (0, 7);
        return Packet.ToByteArray (stream.GetStreamData (), 3);
    }

    /// <summary>
    /// id 16, reserved 2, object_type 10, bit 28, FULL_SPAWN 8; bit 28 is 1 for Token
    /// </summary>
    private static void WriteItemHeader (SphWriteStream stream, ushort entityId, int objectType)
    {
        stream.WriteUInt16 (entityId, 16);
        stream.WriteByte (0, 2);
        stream.WriteUInt16 ((ushort) (objectType & 0x3FF), 10);
        stream.WriteByte (HeaderBit28 (objectType), 1);
        stream.WriteByte (FullSpawn, 8);
    }

    private static byte HeaderBit28 (int objectType) =>
        (ObjectType) objectType is ObjectType.Token ? (byte) 1 : (byte) 0;

    /// <summary>
    /// NoSuffix when the stored suffix is none or unknown
    /// </summary>
    public static int SuffixWireFor (GameObjectType objectType, ItemSuffix suffix)
    {
        if (suffix == ItemSuffix.None)
        {
            return NoSuffix;
        }

        if (GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual.TryGetValue (objectType, out var map) &&
            map.TryGetValue (suffix, out var entry))
        {
            return entry.value;
        }

        return NoSuffix;
    }

    public static int SuffixWireFor (ItemDbEntry item) =>
        SuffixWireFor (item.GameObjectType, item.Suffix);

    /// <summary>
    /// After game_object_id: inverted __hasSuffix, 2-bit length (0 is 3-bit mag, 1 is 7-bit), then
    /// magnitude; ids 0-7 stay six bits, larger ids widen to 10
    /// </summary>
    private static void WriteSuffix (SphWriteStream stream, int suffix)
    {
        if (suffix == NoSuffix)
        {
            stream.WriteByte ((byte) PackedNoSuffix, 6);
            return;
        }

        // Locale ids are about 0-30; map values at 64 and above, or 1090 and above, keep the low 7
        // bits
        var wire = suffix & 0x7F;
        var lengthSelector = wire > 7 ? 1 : 0;
        var magBits = lengthSelector == 0 ? 3 : 7;
        stream.WriteByte (0, 1); // __hasSuffix = 0 → has a suffix
        stream.WriteByte ((byte) lengthSelector, 2);
        stream.WriteByte ((byte) (wire & ((1 << magBits) - 1)), magBits);
    }

    /// <summary>
    /// IntToBits stops on a non-positive value, so a top bit of 1 is dropped; two 16-bit halves
    /// stay positive
    /// </summary>
    private static void WriteUInt32Full (SphWriteStream stream, uint value)
    {
        stream.WriteUInt16 ((ushort) value, 16);
        stream.WriteUInt16 ((ushort) (value >> 16), 16);
    }

    private static bool IsGuildFamily (ObjectType objectType) =>
        objectType is ObjectType.Special_Guild or ObjectType.Special_Ability
            or ObjectType.Special_Ability_Steal or ObjectType.Guild_Specialization;

    private static bool IsParentContainer (ushort containerObjectId) =>
        containerObjectId != 0 && containerObjectId != GroundContainerSentinel;

    /// <summary>
    /// Classic types 976/977/979/982 are not MBC modules; live tags are guild 2000, specab 2001,
    /// specab_ha 2003, and specab by GameObjectType for 982
    /// </summary>
    private static ushort ToLiveGuildTag (ObjectType objectType, GameObjectType gameObjectType)
    {
        return objectType switch
        {
            ObjectType.Special_Guild => (ushort) ObjectType.Guild_Assasin_Rank1,
            ObjectType.Special_Ability => (ushort) ObjectType.Invisibility,
            ObjectType.Special_Ability_Steal => (ushort) ObjectType.Thievery,
            ObjectType.Guild_Specialization => ModuleTagForGuildSpec (gameObjectType),
            _ => (ushort) objectType
        };
    }

    private static ushort ModuleTagForGuildSpec (GameObjectType gameObjectType) => gameObjectType switch
    {
        GameObjectType.Special_Druid_Wolf => (ushort) ObjectType.Steel_Whirlwind,
        GameObjectType.Special_Crusader_Gapclose => (ushort) ObjectType.Divine_Transport,
        GameObjectType.Special_Inquisitor_Teleport => (ushort) ObjectType.Revival,
        GameObjectType.Special_Archmage_Teleport => (ushort) ObjectType.Monastery,
        GameObjectType.Special_Thief_Steal => (ushort) ObjectType.Thievery,
        GameObjectType.Special_MasterOfSteel_Suicide => (ushort) ObjectType.Divine_Wind,
        GameObjectType.Special_Necromancer_Flyer => (ushort) ObjectType.Death_Call,
        GameObjectType.Special_Necromancer_Resurrection => (ushort) ObjectType.Resurrection,
        GameObjectType.Special_Necromancer_Zombie => (ushort) ObjectType.Zombie,
        GameObjectType.Special_Bandier_Flag => (ushort) ObjectType.Raise_Flag,
        GameObjectType.Special_Bandier_DispelControl => (ushort) ObjectType.Unshakable,
        GameObjectType.Special_Bandier_Fortify => (ushort) ObjectType.Fort,
        _ => (ushort) ObjectType.Invisibility
    };

    private static byte[] EncodeLiveGuild (ushort entityId, int objectType, int gameObjectId, int suffix,
        ushort containerObjectId, float x, float y, float z,
        int currentDurability, int maxDurability, GameObjectType gameObjectType)
    {
        var moduleTag = ToLiveGuildTag ((ObjectType) objectType, gameObjectType);
        var inContainer = IsParentContainer (containerObjectId);
        var combined = BuildGuildSpawnSnapshot (entityId, moduleTag, x, y, z,
            containState: (byte) (inContainer ? 2 : 0),
            currentDurability, maxDurability, gameObjectId,
            suffix == NoSuffix ? -1 : suffix);

        if (inContainer)
        {
            combined = Concat (combined, BuildGuildSetParent (entityId, moduleTag, containerObjectId));
        }

        return Concat (combined, BuildGuildProperty0None (entityId, moduleTag));
    }

    private static byte[] BuildGuildSpawnSnapshot (ushort entityId, ushort moduleTag,
        float x, float y, float z, byte containState,
        int currentDurability, int maxDurability, int gameId, int suffixId)
    {
        var stream = GetWriteBitStream ();
        stream.WriteByte (0, 1); // has_position
        stream.WriteUInt16 (0, 15); // tick
        stream.WriteUInt16 (entityId, 16);
        stream.WriteByte (0, 2); // process_id high
        stream.WriteUInt16 ((ushort) (moduleTag & 0xFFF), 12);
        stream.WriteByte (62, 7); // wire = region 61 + 1
        WriteIeeeFloat (stream, x);
        WriteIeeeFloat (stream, y);
        WriteIeeeFloat (stream, z);
        stream.WriteByte (MbcCoordEncoding.EncodeAngle (0), 8);
        stream.WriteByte ((byte) (containState & 3), 2);
        CommonPackets.WriteMbcVarint (stream, currentDurability);
        CommonPackets.WriteMbcVarint (stream, maxDurability);
        CommonPackets.WriteMbcVarint (stream, gameId);
        CommonPackets.WriteMbcVarint (stream, suffixId);
        return Packet.ToByteArray (stream.GetStreamData (), 1);
    }

    private static byte[] BuildGuildSetParent (ushort entityId, ushort moduleTag, ushort containerObjectId)
    {
        return BuildGuildContMan (entityId, moduleTag, command: 11,
        [
            (byte) containerObjectId,
            (byte) (containerObjectId >> 8),
            0
        ]);
    }

    private static byte[] BuildGuildProperty0None (ushort entityId, ushort moduleTag)
    {
        return BuildGuildContMan (entityId, moduleTag, command: 9,
        [
            0,
            0xFF, 0xFF, 0xFF, 0xFF
        ]);
    }

    private static byte[] BuildGuildContMan (ushort entityId, ushort moduleTag, byte command,
        ReadOnlySpan<byte> payload)
    {
        var stream = GetWriteBitStream ();
        stream.WriteByte (0, 1); // has_position
        stream.WriteUInt16 (0, 15); // tick
        stream.WriteUInt16 (entityId, 16);
        stream.WriteByte (0, 2); // process_id high
        stream.WriteUInt16 ((ushort) (moduleTag & 0xFFF), 12);
        stream.WriteByte (5, 7); // wire = region 4 + 1 (ContMan)
        stream.WriteByte (command, 8);
        stream.WriteByte ((byte) payload.Length, 8);
        foreach (var b in payload)
        {
            stream.WriteByte (b, 8);
        }

        return Packet.ToByteArray (stream.GetStreamData (), 1);
    }

    private static byte[] Concat (byte[] first, byte[] second)
    {
        var combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy (first, 0, combined, 0, first.Length);
        Buffer.BlockCopy (second, 0, combined, first.Length, second.Length);
        return combined;
    }

    private static void WriteIeeeFloat (SphWriteStream stream, float value)
    {
        var bits = BitConverter.SingleToUInt32Bits (value);
        stream.WriteUInt16 ((ushort) bits, 16);
        stream.WriteUInt16 ((ushort) (bits >> 16), 16);
    }
}
