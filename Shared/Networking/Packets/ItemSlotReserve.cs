using System;
using System.Collections.Generic;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
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
    public const int NoItem = 0;

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

    public static byte[]? Build (ushort clientIndex, BelongingSlot slot, int itemId, int count = 1)
    {
        var wireSlot = WireSlotId (slot);
        if (wireSlot is null)
        {
            // MainHand has no cell; any other missing wire id would truncate to 8 bits and claim an
            // unrelated slot
            if (slot is not BelongingSlot.MainHand)
            {
                SphLogger.Warning ($"ItemSlotReserve: no wire slot for {slot} - [skip]. " +
                                  $"Client ID: {clientIndex:X4}");
            }

            return null;
        }

        return BuildRaw (clientIndex, wireSlot.Value, itemId, count);
    }

    /// <summary>
    /// slot_id cannot be 0 or a helmet is never drawn; no count, so stacks still need the reserve
    /// </summary>
    public static byte[] BuildSlotBinding (ushort clientIndex, BelongingSlot slot, int itemId) =>
        BuildMove (clientIndex, slot, slot, itemId);

    /// <summary>
    /// The client does not move or swap on its own, so the window follows this packet
    /// </summary>
    public static byte[] BuildMove (ushort clientIndex, BelongingSlot from, BelongingSlot to, int itemId)
    {
        var fromRaw = (byte) ((int) from << 1);
        var toRaw = (byte) ((int) to << 1);
        var id = (ushort) itemId;

        return
        [
            0x20, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte (clientIndex), MinorByte (clientIndex),
            0x08, 0x40, 0x41, 0x10,
            fromRaw, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x0A, 0x82,
            (byte) ((toRaw & 0b11111) << 3),
            (byte) (((id & 0b1111) << 4) + (toRaw >> 5)),
            (byte) ((id >> 4) & 0xFF),
            (byte) (id >> 12),
            0xC0, 0x44, 0x00, 0x00, 0x00
        ];
    }

    /// <summary>
    /// Raw wire slot id for slots BelongingSlot does not name
    /// </summary>
    public static byte[] BuildRaw (ushort clientIndex, int wireSlot, int itemId, int count = 1)
    {
        var parts = PacketPart.LoadDefinedWithOverride ("new_item_reserve_slot_full");
        PacketPart.UpdateEntityId (parts, ByteSwap (clientIndex));
        PacketPart.UpdateValue (parts, "slot_id", wireSlot, 8);
        PacketPart.UpdateValue (parts, "new_item_id", itemId, 16);
        PacketPart.UpdateValue (parts, "count_if_present", count < 1 ? 1 : count, 8);
        return PacketPart.GetBytesToWrite (parts);
    }
}
