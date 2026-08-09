using System;
using SphereHelpers.Extensions;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db.DataModels;
using static SphServer.Shared.BitStream.SphBitStream;

namespace SphServer.Packets;

/// <summary>
///     The server->client item record: this item exists, and it belongs to that container.
///     containerObjectId is the id the client itself knows the container by — a character is on the
///     wire as ByteSwap(ClientIndex), so a caller naming the player has to swap it.
/// </summary>
public static class ItemRecordEncoder
{
    private const int FullSpawn = 0x7C;

    /// <summary>Position an item carries while it is inside a container rather than lying in the world.</summary>
    private const float ContainedX = 1000000.0f;

    /// <summary>Placement and orientation state; the variants seen in captures are all ground drops.</summary>
    private const uint PlacementState = 0x322C8A00;

    /// <summary>Follow-on message: a 7-bit marker, then type and length, then the payload.</summary>
    private const uint FollowOnRecord = 5;

    private const uint PutHereMessage = 0;
    private const uint PropertiesMessage = 9;

    /// <summary>
    ///     Sentinel for Encode/SuffixWireFor: item has no suffix. Not an on-wire magnitude —
    ///     Actual locale ids are 0..N and include 17, so the packed none pattern lives in
    ///     <see cref="PackedNoSuffix"/>.
    /// </summary>
    public const int NoSuffix = -1;

    /// <summary>
    ///     On-wire none: __hasSuffix(1) + suffix_length(0) + suffix(2) = six bits.
    /// </summary>
    private const int PackedNoSuffix = 17;

    /// <summary>
    ///     The shorter shape, carrying no game object id: the item is identified by object type
    ///     alone, so every item of a type shares one icon.
    /// </summary>
    public static byte[] EncodeWithoutGameId(ushort entityId, int objectType, ushort containerObjectId,
        float x = ContainedX, float y = 0f, float z = 0f)
    {
        var stream = GetWriteBitStream();
        stream.WriteUInt16(entityId, 16);
        stream.WriteByte(0, 2);
        stream.WriteUInt16((ushort)(objectType & 0x3FF), 10);
        stream.WriteByte(0, 1);
        stream.WriteByte(FullSpawn, 8);
        WriteUInt32Full(stream, BitConverter.SingleToUInt32Bits(x));
        WriteUInt32Full(stream, BitConverter.SingleToUInt32Bits(y));
        WriteUInt32Full(stream, BitConverter.SingleToUInt32Bits(z));
        stream.WriteByte(0, 8);
        stream.WriteUInt32(0x322C89, 24);
        stream.WriteByte(0, 1);
        WriteUInt32Full(stream, 0x05090A89);
        stream.WriteUInt16(containerObjectId, 16);
        stream.WriteUInt32(0x7FFFFF, 23);
        return Packet.ToByteArray(stream.GetStreamData(), 3);
    }

    public static byte[] Encode(ushort entityId, int objectType, int gameObjectId, int suffix,
        ushort containerObjectId, float x = ContainedX, float y = 0f, float z = 0f)
    {
        var stream = GetWriteBitStream();
        stream.WriteUInt16(entityId, 16);
        stream.WriteByte(0, 2);
        stream.WriteUInt16((ushort)(objectType & 0x3FF), 10);
        stream.WriteByte(0, 1);
        stream.WriteByte(FullSpawn, 8);
        WriteUInt32Full(stream, BitConverter.SingleToUInt32Bits(x));
        WriteUInt32Full(stream, BitConverter.SingleToUInt32Bits(y));
        WriteUInt32Full(stream, BitConverter.SingleToUInt32Bits(z));
        WriteUInt32Full(stream, PlacementState);
        stream.WriteByte(1, 1);

        // Masked to their field widths: the writer sizes a value by its own magnitude, so one that
        // does not fit would lengthen the record and shift everything after it.
        stream.WriteUInt16((ushort)(gameObjectId & 0x3FFF), 14);
        WriteSuffix(stream, suffix);

        // The two messages that go to the item's own script. Their type field is eight bits wide in
        // this client's modules, where the 2022 build used four.
        // "You are inside this container" — the only thing that gives an item a parent.
        stream.WriteByte((byte)FollowOnRecord, 7);
        stream.WriteByte((byte)PutHereMessage, 8);
        stream.WriteByte(3, 8);
        stream.WriteUInt32(containerObjectId, 24);

        // The item's own properties. Property 0 stays -1: any other value that is not the player's
        // own id trips a second refusal, separate from the parent check.
        stream.WriteByte((byte)FollowOnRecord, 7);
        stream.WriteByte((byte)PropertiesMessage, 8);
        stream.WriteByte(5, 8);
        stream.WriteByte(0, 8);
        WriteUInt32Full(stream, uint.MaxValue);

        stream.WriteByte(0, 7);
        return Packet.ToByteArray(stream.GetStreamData(), 3);
    }

    /// <summary>
    ///     Map an item's stored <see cref="ItemSuffix"/> to the wire id Encode expects
    ///     (<see cref="NoSuffix"/> when none / unknown).
    /// </summary>
    public static int SuffixWireFor(GameObjectType objectType, ItemSuffix suffix)
    {
        if (suffix == ItemSuffix.None)
        {
            return NoSuffix;
        }

        if (GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual.TryGetValue(objectType, out var map) &&
            map.TryGetValue(suffix, out var entry))
        {
            return entry.value;
        }

        return NoSuffix;
    }

    public static int SuffixWireFor(ItemDbEntry item) =>
        SuffixWireFor(item.GameObjectType, item.Suffix);

    /// <summary>
    ///     Suffix field after game_object_id. Same layout as NpcInteractableSerializer / item_with_gameid:
    ///     inverted __hasSuffix bit, 2-bit length selector (0→3-bit mag, 1→7-bit mag), then magnitude.
    ///     ObjectTypeToSuffixLocaleMapActual ids are small (0..N); width follows the value so ids
    ///     0–7 keep the six-bit field width (swords: Valor=1, Damage=2, …). Larger ids widen to 10 bits.
    /// </summary>
    private static void WriteSuffix(SphWriteStream stream, int suffix)
    {
        if (suffix == NoSuffix)
        {
            stream.WriteByte((byte)PackedNoSuffix, 6);
            return;
        }

        // Actual ids are 0..~30; legacy map values 64+ / 1090+ keep the low 7 bits (PacketPart path).
        var wire = suffix & 0x7F;
        var lengthSelector = wire > 7 ? 1 : 0;
        var magBits = lengthSelector == 0 ? 3 : 7;
        stream.WriteByte(0, 1); // __hasSuffix = 0 → has a suffix
        stream.WriteByte((byte)lengthSelector, 2);
        stream.WriteByte((byte)(wire & ((1 << magBits) - 1)), magBits);
    }

    /// <summary>
    ///     Writes all 32 bits. The shared writer sizes a value through IntToBits(int, ...), whose
    ///     loop runs while the value is positive, so anything with the top bit set is dropped and the
    ///     field padded with zeros. Two halves are always positive.
    /// </summary>
    private static void WriteUInt32Full(SphWriteStream stream, uint value)
    {
        stream.WriteUInt16((ushort)value, 16);
        stream.WriteUInt16((ushort)(value >> 16), 16);
    }
}
