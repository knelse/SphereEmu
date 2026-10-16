using System;
using System.Collections.Generic;
using SphereHelpers.Extensions;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using static SphServer.Shared.BitStream.SphBitStream;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Packets;

/// <summary>
/// The window draws from this array, so a cell stays empty until it holds a live handle; this goes
/// out before that item record
/// </summary>
public static class ItemSlotReserve
{
    /// <summary>
    /// A handle that resolves to nothing, used when clearing a slot
    /// </summary>
    public static readonly Guid NoItem = Guid.Empty;

    /// <summary>
    /// BelongingSlot numbers these from 1000, past the 8-bit field; ids follow the serializer slot
    /// order, two bytes per slot
    /// </summary>
    private static readonly Dictionary<BelongingSlot, int> WireSlotOverrides = new ()
    {
        [BelongingSlot.Money] = 21,
        [BelongingSlot.Backpack] = 22,
        [BelongingSlot.Key_1] = 23,
        [BelongingSlot.Key_2] = 24,
        [BelongingSlot.Mission] = 25,
        [BelongingSlot.Special_1] = 46,
        [BelongingSlot.Special_2] = 47,
        [BelongingSlot.Special_3] = 48,
        [BelongingSlot.Special_4] = 49,
        [BelongingSlot.Special_5] = 50,
        [BelongingSlot.Special_6] = 51,
        [BelongingSlot.Special_7] = 52,
        [BelongingSlot.Special_8] = 53,
        [BelongingSlot.Special_9] = 54,
    };

    /// <summary>
    /// Null when the wire id names no slot
    /// </summary>
    public static BelongingSlot? SlotForWireId (int wireSlot)
    {
        foreach (var (slot, wire) in WireSlotOverrides)
        {
            if (wire == wireSlot)
            {
                return slot;
            }
        }

        // The enum has no members at the overridden ids, so this cast cannot shadow them
        return Enum.IsDefined (typeof (BelongingSlot), wireSlot) ? (BelongingSlot) wireSlot : null;
    }

    /// <summary>
    /// Null when that slot has no known 8-bit wire id
    /// </summary>
    public static int? WireSlotId (BelongingSlot slot)
    {
        if (WireSlotOverrides.TryGetValue (slot, out var wire))
        {
            return wire;
        }

        return (int) slot is >= 0 and <= 45 ? (int) slot : null;
    }

    public static byte[]? Build (Guid clientIndex, BelongingSlot slot, Guid itemId, int count = 1)
    {
        var wireSlot = WireSlotId (slot);
        if (wireSlot is null)
        {
            // MainHand has no cell; any other missing wire id would truncate to 8 bits and claim an
            // unrelated slot
            if (slot is not BelongingSlot.MainHand)
            {
                SphLogger.Warning ($"ItemSlotReserve: no wire slot for {slot} - [skip]. " +
                                  $"Client ID: {clientIndex}");
            }

            return null;
        }

        return BuildRaw (clientIndex, wireSlot.Value, itemId, count);
    }

    /// <summary>
    /// slot_id cannot be 0 or a helmet is never drawn; no count, so stacks still need the reserve
    /// </summary>
    public static byte[] BuildSlotBinding (Guid clientIndex, BelongingSlot slot, Guid itemId) =>
        BuildMove (clientIndex, slot, slot, itemId);

    /// <summary>
    /// The client does not move or swap on its own, so the window follows this packet
    /// </summary>
    public static byte[] BuildMove (Guid clientIndex, BelongingSlot from, BelongingSlot to, Guid itemId)
    {
        var fromRaw = (byte) ((int) from << 1);
        var toRaw = (byte) ((int) to << 1);
        var stream = GetWriteBitStream ();
        stream.WriteByte (0, 1);
        stream.WriteUInt16 (0, 15);
        stream.WriteGuid (clientIndex);
        stream.WriteBytes ([0x08, 0x40, 0x41, 0x10]);
        stream.WriteByte (fromRaw, 8);
        stream.WriteBytes ([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        stream.WriteBytes ([0x0A, 0x82]);
        stream.WriteByte (toRaw, 8);
        stream.WriteGuid (itemId);
        stream.WriteBytes ([0xC0, 0x44, 0x00, 0x00, 0x00]);
        return Packet.ToByteArray (stream.GetStreamData (), 1);
    }

    /// <summary>
    /// Raw wire slot id for slots BelongingSlot does not name
    /// </summary>
    public static byte[] BuildRaw (Guid clientIndex, int wireSlot, Guid itemId, int count = 1)
    {
        var parts = PacketPart.LoadDefinedWithOverride ("new_item_reserve_slot_full");
        PacketPart.UpdateEntityId (parts, clientIndex);
        PacketPart.UpdateValue (parts, "slot_id", wireSlot, 8);
        PacketPart.UpdateGuid (parts, "new_item_id", itemId);
        PacketPart.UpdateValue (parts, "count_if_present", count < 1 ? 1 : count, 8);
        return PacketPart.GetBytesToWrite (parts);
    }
}
