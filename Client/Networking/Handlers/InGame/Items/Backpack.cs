using System;
using System.Collections.Generic;
using System.Linq;
using SphereHelpers.Extensions;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using static SphServer.Shared.BitStream.SphBitStream;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

/// <summary>
/// A backpack (ct_bag and kin) is a container of its own: the client draws its window from the
/// cells the server sent it and opens it without asking, so the cells go out with the bag
/// </summary>
public static class Backpack
{
    /// <summary>
    /// GetSlotsNum of each module
    /// </summary>
    private static readonly Dictionary<ObjectType, int> CellCounts = new ()
    {
        [ObjectType.Backpack_Large] = 12,
        [ObjectType.Backpack_Small] = 8,
        [ObjectType.Backpack_Great] = 24,
        [ObjectType.Sack] = 4,
        [ObjectType.Sac_M] = 8
    };

    private const byte FollowOnMarker = 5;
    private const byte LockMessage = 3;
    private const byte SlotMessage = 2;

    public static bool IsBackpack (ItemDbEntry item) => CellCounts.ContainsKey (item.WireObjectType);

    public static int CellCount (ItemDbEntry bag) => CellCounts.GetValueOrDefault (bag.WireObjectType);

    /// <summary>
    /// Item records first: the cells bind to item processes that must already exist
    /// </summary>
    public static List<byte[]> GetContentsPackets (ItemDbEntry bag)
    {
        var packets = GetItems (bag).Select (x => ItemRecordEncoder.Encode (x, (ushort) bag.Id)).ToList ();
        packets.Add (BuildCellsPacket (bag));
        return packets;
    }

    /// <summary>
    /// Unlock, then every cell: ContMan cmd 2 with slot, 24-bit item id and 32-bit weight, or the
    /// slot alone for an empty one, as the loot sack list does
    /// </summary>
    public static byte[] BuildCellsPacket (ItemDbEntry bag)
    {
        var stream = SphServer.Shared.BitStream.SphBitStream.GetWriteBitStream ();
        stream.WriteUInt16 ((ushort) bag.Id, 16);
        stream.WriteByte (0, 2);
        stream.WriteUInt16 ((ushort) ((int) bag.WireObjectType & 0xFFF), 12);

        WriteMessageHeader (stream, LockMessage, 1);
        stream.WriteByte (0, 8);

        for (var cell = 0; cell < CellCount (bag); cell++)
        {
            var item = bag.BackpackContents.TryGetValue (cell, out var itemId)
                ? DbConnection.Items.FindById (itemId)
                : null;
            if (item is null)
            {
                WriteMessageHeader (stream, SlotMessage, 1);
                stream.WriteByte ((byte) cell, 8);
                continue;
            }

            WriteMessageHeader (stream, SlotMessage, 8);
            stream.WriteByte ((byte) cell, 8);
            stream.WriteUInt16 ((ushort) item.Id, 16);
            stream.WriteByte ((byte) (item.Id >> 16), 8);
            var weight = (uint) Math.Clamp ((long) item.Weight * Math.Max (1, item.ItemCount), 0, int.MaxValue);
            stream.WriteUInt16 ((ushort) weight, 16);
            stream.WriteUInt16 ((ushort) (weight >> 16), 16);
        }

        stream.WriteByte (0, 7);
        return Packet.ToByteArray (stream.GetStreamData (), 3);
    }

    /// <summary>
    /// An inventory item into a free cell of the worn backpack. The reply mirrors the sack pickup:
    /// the player's cell empties, the item takes the bag as parent, the bag's cells are redrawn.
    /// A count below the stack moves only that many, as an item of its own
    /// </summary>
    public static bool PutIn (ClientConnection clientConnection, ItemDbEntry bag, ItemDbEntry item, int cell,
        int count = 0)
    {
        var splitting = IsSplit (item, count);
        var localId = clientConnection.LocalId;
        var character = clientConnection.GetSelectedCharacter ();
        var from = character?.Items.FirstOrDefault (x => x.Value == item.Id && x.Key != BelongingSlot.MainHand);
        string? refusal = null;
        if (character is null || from?.Value != item.Id)
        {
            refusal = "not carried";
        }
        else if (!ItemDbEntry.IsInventorySlot (from.Value.Key))
        {
            refusal = $"worn in {Enum.GetName (from.Value.Key)}";
        }
        // MainHand holds a reference to an inventory item, so the hand would point into the bag
        else if (!splitting && character.Items.TryGetValue (BelongingSlot.MainHand, out var inHand) &&
                 inHand == item.Id)
        {
            refusal = "held in hand";
        }
        else if (IsBackpack (item))
        {
            refusal = "a backpack";
        }
        else if (cell < 0 || cell >= CellCount (bag) || bag.BackpackContents.ContainsKey (cell))
        {
            refusal = $"cell {cell} is taken or out of range";
        }

        if (refusal is not null)
        {
            Log (localId, bag, item, $"not put into cell {cell}: {refusal}");
            Redraw (clientConnection, bag, from?.Key);
            return false;
        }

        var slot = from!.Value.Key;
        if (splitting)
        {
            var part = SplitOff (item, count);
            part.ParentBackpackId = bag.Id;
            DbConnection.Items.Update (part);
            bag.BackpackContents[cell] = part.Id;
            DbConnection.Items.Update (bag);

            clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.EncodeCount (item));
            if (ItemSlotReserve.Build (localId, slot, item.Id, item.ItemCount) is { } kept)
            {
                clientConnection.MaybeScheduleNetworkPacketSend (kept);
            }

            clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.Encode (part, (ushort) bag.Id));
            clientConnection.MaybeScheduleNetworkPacketSend (BuildCellsPacket (bag));
            Log (localId, bag, part, $"{count} of {item.ItemCount + count} split off {Enum.GetName (slot)} " +
                                     $"[{item.Id:X4}] -> cell {cell}");
            return true;
        }

        character!.Items.Remove (slot);
        bag.BackpackContents[cell] = item.Id;
        item.ParentBackpackId = bag.Id;
        DbConnection.Items.Update (bag);
        DbConnection.Items.Update (item);
        clientConnection.SaveSelectedCharacter ();

        if (ItemSlotReserve.Build (localId, slot, ItemSlotReserve.NoItem) is { } vacated)
        {
            clientConnection.MaybeScheduleNetworkPacketSend (vacated);
        }

        clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.EncodePutInto (item, (ushort) bag.Id));
        clientConnection.MaybeScheduleNetworkPacketSend (BuildCellsPacket (bag));
        Log (localId, bag, item, $"{Enum.GetName (slot)} -> cell {cell}");
        return true;
    }

    /// <summary>
    /// Out of the bag into a player slot; a non-inventory target must also take the item. A count
    /// below the stack takes only that many and leaves the rest in the cell
    /// </summary>
    public static bool TakeOut (ClientConnection clientConnection, ItemDbEntry item, BelongingSlot? wanted,
        int count = 0)
    {
        var localId = clientConnection.LocalId;
        var character = clientConnection.GetSelectedCharacter ();
        var bag = item.ParentBackpackId is { } bagId ? DbConnection.Items.FindById (bagId) : null;
        var cell = bag?.BackpackContents.FirstOrDefault (x => x.Value == item.Id);
        if (character is null || bag is null || cell?.Value != item.Id)
        {
            SphLogger.Warning ($"Backpack: item {item.Id:X4} is not in a backpack cell. Client ID: {localId:X4}");
            return false;
        }

        var slot = wanted is { } target && target is not BelongingSlot.Unknown && character.IsItemSlotEmpty (target) &&
                   (ItemDbEntry.IsInventorySlot (target) || (item.IsValidForSlot (target) && character.CanUseItem (item)))
            ? target
            : character.FindEmptyInventorySlot ();
        if (slot is null)
        {
            Log (localId, bag, item, "not taken out: inventory is full");
            Redraw (clientConnection, bag, null);
            return false;
        }

        if (IsSplit (item, count))
        {
            var part = SplitOff (item, count);
            character.PlaceItemInSlot (slot.Value, part.Id);
            clientConnection.SaveSelectedCharacter ();

            clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.EncodeCount (item));
            clientConnection.MaybeScheduleNetworkPacketSend (BuildCellsPacket (bag));
            clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.Encode (part, ByteSwap (localId)));
            if (ItemSlotReserve.Build (localId, slot.Value, part.Id, part.ItemCount) is { } claimed)
            {
                clientConnection.MaybeScheduleNetworkPacketSend (claimed);
            }

            Log (localId, bag, part, $"{count} of {item.ItemCount + count} split off cell {cell.Value.Key} " +
                                     $"[{item.Id:X4}] -> {Enum.GetName (slot.Value)}");
            return true;
        }

        bag.BackpackContents.Remove (cell.Value.Key);
        item.ParentBackpackId = null;
        character.PlaceItemInSlot (slot.Value, item.Id);
        DbConnection.Items.Update (bag);
        DbConnection.Items.Update (item);

        clientConnection.MaybeScheduleNetworkPacketSend (BuildCellsPacket (bag));
        clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.EncodePutInto (item, ByteSwap (localId)));
        if (ItemSlotReserve.Build (localId, slot.Value, item.Id, item.ItemCount) is { } reserve)
        {
            clientConnection.MaybeScheduleNetworkPacketSend (reserve);
        }

        if (!ItemDbEntry.IsInventorySlot (slot.Value) && character.RecalcCurrentStats ())
        {
            GameplayLogic.Stats.NetworkedStatsUpdater.Update (character);
        }

        // Recalc writes worn look and item stats, so a save before it would miss them
        clientConnection.SaveSelectedCharacter ();
        Log (localId, bag, item, $"cell {cell.Value.Key} -> {Enum.GetName (slot.Value)}");
        return true;
    }

    /// <summary>
    /// Between two cells of one bag; an occupied target swaps, and a count below the stack splits
    /// that many into an empty target
    /// </summary>
    public static void MoveCell (ClientConnection clientConnection, ItemDbEntry bag, int from, int to, int count = 0)
    {
        if (from == to || to < 0 || to >= CellCount (bag) || !bag.BackpackContents.TryGetValue (from, out var itemId))
        {
            SphLogger.Info ($"Backpack {bag.Id:X4}: no move from cell {from} to {to}. " +
                            $"Client ID: {clientConnection.LocalId:X4}");
            Redraw (clientConnection, bag, null);
            return;
        }

        if (count > 0 && DbConnection.Items.FindById (itemId) is { } stack && IsSplit (stack, count))
        {
            if (bag.BackpackContents.ContainsKey (to))
            {
                SphLogger.Info ($"Backpack {bag.Id:X4}: no split from cell {from} into taken cell {to}. " +
                                $"Client ID: {clientConnection.LocalId:X4}");
                Redraw (clientConnection, bag, null);
                return;
            }

            var part = SplitOff (stack, count);
            part.ParentBackpackId = bag.Id;
            DbConnection.Items.Update (part);
            bag.BackpackContents[to] = part.Id;
            DbConnection.Items.Update (bag);
            clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.EncodeCount (stack));
            clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.Encode (part, (ushort) bag.Id));
            clientConnection.MaybeScheduleNetworkPacketSend (BuildCellsPacket (bag));
            SphLogger.Info ($"Backpack {bag.Id:X4}: {count} split off cell {from} into cell {to} as {part.Id:X4}. " +
                            $"Client ID: {clientConnection.LocalId:X4}");
            return;
        }

        if (bag.BackpackContents.TryGetValue (to, out var other))
        {
            bag.BackpackContents[from] = other;
        }
        else
        {
            bag.BackpackContents.Remove (from);
        }

        bag.BackpackContents[to] = itemId;
        DbConnection.Items.Update (bag);
        clientConnection.MaybeScheduleNetworkPacketSend (BuildCellsPacket (bag));
        SphLogger.Info ($"Backpack {bag.Id:X4}: cell {from} -> {to}. Client ID: {clientConnection.LocalId:X4}");
    }

    /// <summary>
    /// The window may already have moved the item on its own, so a refusal puts both sides back
    /// </summary>
    public static void Redraw (ClientConnection clientConnection, ItemDbEntry bag, BelongingSlot? playerSlot)
    {
        clientConnection.MaybeScheduleNetworkPacketSend (BuildCellsPacket (bag));
        var character = clientConnection.GetSelectedCharacter ();
        if (playerSlot is { } slot && character is not null && character.Items.TryGetValue (slot, out var itemId) &&
            ItemSlotReserve.Build (clientConnection.LocalId, slot, itemId,
                DbConnection.Items.FindById (itemId)?.ItemCount ?? 1) is { } reserve)
        {
            clientConnection.MaybeScheduleNetworkPacketSend (reserve);
        }
    }

    private static bool IsSplit (ItemDbEntry item, int count) => count > 0 && count < item.ItemCount;

    /// <summary>
    /// The clone shares the source's cell link, which the caller sets for the part's own place
    /// </summary>
    private static ItemDbEntry SplitOff (ItemDbEntry item, int count)
    {
        var part = item.SplitOff (count);
        part.ParentBackpackId = null;
        part.BackpackContents = new ();
        DbConnection.Items.Update (part);
        return part;
    }

    private static List<ItemDbEntry> GetItems (ItemDbEntry bag) =>
        bag.BackpackContents.OrderBy (x => x.Key)
            .Select (x => DbConnection.Items.FindById (x.Value))
            .Where (x => x is not null)
            .ToList ();

    private static void WriteMessageHeader (SphServer.Shared.BitStream.SphWriteStream stream, byte type, byte length)
    {
        // Backpack modules read an 8-bit command (sfera_protocol_schema: u8, array8<u8>)
        stream.WriteByte (FollowOnMarker, 7);
        stream.WriteByte (type, 8);
        stream.WriteByte (length, 8);
    }

    private static void Log (ushort localId, ItemDbEntry bag, ItemDbEntry item, string outcome)
    {
        SphLogger.Info ($"Backpack {bag.Id:X4}: {item.Localization.GetValueOrDefault (Locale.Russian, "?")} " +
                        $"[{item.Id:X4}] {outcome}. Client ID: {localId:X4}");
    }
}
