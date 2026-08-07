using System;
using System.Collections.Generic;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using static SphServer.Shared.BitStream.SphBitStream;

namespace SphServer.Packets;

/// <summary>
///     Which of the character's slots holds which item. The window draws from this array, so a cell
///     stays empty until it holds a live handle. Send it before the item record it points at.
/// </summary>
public static class ItemSlotReserve
{
    /// <summary>Clearing a slot: the array entry is set to a handle that resolves to nothing.</summary>
    public const int NoItem = 0;

    /// <summary>
    ///     BelongingSlot numbers these from 1000, which does not fit the 8-bit field. Their real ids
    ///     come from CharacterDbEntrySerializer's slot order, two bytes per slot.
    /// </summary>
    private static readonly Dictionary<BelongingSlot, int> WireSlotOverrides = new()
    {
        [BelongingSlot.Money] = 21,
        [BelongingSlot.Backpack] = 22,
        [BelongingSlot.Key_1] = 23,
        [BelongingSlot.Key_2] = 24,
        [BelongingSlot.Mission] = 25,
    };

    /// <summary>The id this slot has on the wire, or null when we do not know it.</summary>
    public static int? WireSlotId (BelongingSlot slot)
    {
        if (WireSlotOverrides.TryGetValue(slot, out var wire))
        {
            return wire;
        }

        return (int) slot is >= 0 and <= 45 ? (int) slot : null;
    }

    public static byte[]? Build (ushort clientIndex, BelongingSlot slot, int itemId, int count = 1)
    {
        var wireSlot = WireSlotId(slot);
        if (wireSlot is null)
        {
            // The hand is held rather than worn and has no cell to reserve, so it is not a fault.
            // Anything else would truncate to eight bits and claim an unrelated slot.
            if (slot is not BelongingSlot.MainHand)
            {
                SphLogger.Warning($"ItemSlotReserve: no wire slot for {slot} - [skip]. " +
                                  $"Client ID: {clientIndex:X4}");
            }

            return null;
        }

        return BuildRaw(clientIndex, wireSlot.Value, itemId, count);
    }

    private static byte[] BuildRaw (ushort clientIndex, int wireSlot, int itemId, int count = 1)
    {
        var parts = PacketPart.LoadDefinedWithOverride("new_item_reserve_slot_full");
        PacketPart.UpdateEntityId(parts, ByteSwap(clientIndex));
        PacketPart.UpdateValue(parts, "slot_id", wireSlot, 8);
        PacketPart.UpdateValue(parts, "new_item_id", itemId, 16);
        // Clamped, not just floored: the writer pads a short value but never trims a
        // long one, so a count above 255 would lengthen the frame.
        PacketPart.UpdateValue(parts, "count_if_present", Math.Clamp(count, 1, 255), 8);
        return PacketPart.GetBytesToWrite(parts);
    }
}
