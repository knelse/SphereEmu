using System.Collections.Generic;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using static SphServer.Shared.BitStream.SphBitStream;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

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

    /// <summary>
    ///     Binds a slot to an item the way a move does, for the one slot the reserve above cannot
    ///     name. Its slot_id will not carry 0, so a helmet declared with it is never drawn — while
    ///     the same slot filled by dragging works, because dragging sends this shape instead.
    ///     Retail declares carried items this way too: the 0A 82 record appears in login captures
    ///     for the bank, key and inkpot slots. Carries no count, so stacked items still need the
    ///     reserve.
    /// </summary>
    public static byte[] BuildSlotBinding (ushort clientIndex, BelongingSlot slot, int itemId)
    {
        var raw = (byte) ((int) slot << 1);
        var id = (ushort) itemId;

        return
        [
            0x20, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(clientIndex), MinorByte(clientIndex),
            0x08, 0x40, 0x41, 0x10,
            raw, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x0A, 0x82,
            (byte) ((raw & 0b11111) << 3),
            (byte) (((id & 0b1111) << 4) + (raw >> 5)),
            (byte) ((id >> 4) & 0xFF),
            (byte) (id >> 12),
            0xC0, 0x44, 0x00, 0x00, 0x00
        ];
    }

    /// <summary>A raw wire slot id, for probing the ones BelongingSlot has no name for.</summary>
    public static byte[] BuildRaw (ushort clientIndex, int wireSlot, int itemId, int count = 1)
    {
        var parts = PacketPart.LoadDefinedWithOverride("new_item_reserve_slot_full");
        PacketPart.UpdateEntityId(parts, ByteSwap(clientIndex));
        PacketPart.UpdateValue(parts, "slot_id", wireSlot, 8);
        PacketPart.UpdateValue(parts, "new_item_id", itemId, 16);
        PacketPart.UpdateValue(parts, "count_if_present", count < 1 ? 1 : count, 8);
        return PacketPart.GetBytesToWrite(parts);
    }
}
