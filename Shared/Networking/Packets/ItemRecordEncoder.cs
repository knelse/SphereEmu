using System;
using SphereHelpers.Extensions;
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
    ///     No suffix, packed the way the field wants it: a sign bit, a two-bit width selector, and a
    ///     magnitude. Selector 0 makes the field six bits, which is the only width Encode can write —
    ///     a real suffix id widens it and moves everything after it.
    /// </summary>
    public const int NoSuffix = 17;

    public static byte[] Encode (ushort entityId, int objectType, int gameObjectId, int suffix,
        ushort containerObjectId, float x = ContainedX, float y = 0f, float z = 0f)
    {
        var stream = GetWriteBitStream();
        stream.WriteUInt16(entityId, 16);
        stream.WriteByte(0, 2);
        stream.WriteUInt16((ushort) (objectType & 0x3FF), 10);
        stream.WriteByte(0, 1);
        stream.WriteByte(FullSpawn, 8);
        stream.WriteUInt32(BitConverter.SingleToUInt32Bits(x), 32);
        stream.WriteUInt32(BitConverter.SingleToUInt32Bits(y), 32);
        stream.WriteUInt32(BitConverter.SingleToUInt32Bits(z), 32);
        stream.WriteUInt32(PlacementState, 32);
        stream.WriteByte(1, 1);

        // Masked to their field widths: the writer sizes a value by its own magnitude, so one that
        // does not fit would lengthen the record and shift everything after it.
        stream.WriteUInt16((ushort) (gameObjectId & 0x3FFF), 14);
        stream.WriteByte((byte) (suffix & 0x3F), 6);

        // The two messages that go to the item's own script. Their type field is eight bits wide in
        // this client's modules, where the 2022 build used four.
        // "You are inside this container" — the only thing that gives an item a parent.
        stream.WriteByte((byte) FollowOnRecord, 7);
        stream.WriteByte((byte) PutHereMessage, 8);
        stream.WriteByte(3, 8);
        stream.WriteUInt32(containerObjectId, 24);

        // The item's own properties. Property 0 stays -1: any other value that is not the player's
        // own id trips a second refusal, separate from the parent check.
        stream.WriteByte((byte) FollowOnRecord, 7);
        stream.WriteByte((byte) PropertiesMessage, 8);
        stream.WriteByte(5, 8);
        stream.WriteByte(0, 8);
        stream.WriteUInt32(uint.MaxValue, 32);

        stream.WriteByte(0, 7);
        return Packet.ToByteArray(stream.GetStreamData(), 3);
    }
}
