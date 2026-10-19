using System;
using System.Linq;
using System.Threading.Tasks;
using SphereHelpers.Extensions;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Helpers.Networking;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

/// <summary>
/// A stack dropped on a stack of the same item: _inv does not move it into the cell but sends the
/// dragged item's use on the target (cuse with player, target and amount) and leaves both cells to
/// the server's count messages
/// </summary>
public class StackItemsHandler (ushort localId, ClientConnection clientConnection, SphereClient client)
    : ISphereClientNetworkingHandler
{
    /// <summary>
    /// The client sets no limit; the server's count message (ContMan cmd 12) carries 16 bits
    /// </summary>
    public const int MaxStackSize = ushort.MaxValue;

    private const int PlayerIdWidth = 24;

    public Task Handle (byte[] frame, double delta)
    {
        // Item use arms g_6008, so this ack clears it or the client stays wedged
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));

        var sourceId = frame[11] + frame[12] * 0x100;
        var stream = new BitStreams.BitStream (frame);
        stream.ReadBits (GameplayRecord.ObjectPayloadBit (frame) + PlayerIdWidth);
        var targetId = (int) stream.ReadUInt32 (24);
        var amount = (int) stream.ReadUInt32 (24);

        var source = DbConnection.Items.FindById (sourceId);
        var target = DbConnection.Items.FindById (targetId);
        if (source is null || target is null)
        {
            SphLogger.Warning ($"Stack: no item {(source is null ? sourceId : targetId):X4}. Client ID: {localId:X4}");
            return Task.CompletedTask;
        }

        Merge (client, source, target, amount);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Moves up to the amount (all of it when the dialog was skipped) until the target is full;
    /// an emptied source goes away as a used-up item does
    /// </summary>
    public static bool Merge (SphereClient client, ItemDbEntry source, ItemDbEntry target, int amount)
    {
        var character = client.CurrentCharacter;
        var clientId = character?.ClientIndex ?? 0;
        var sourceCell = character is null ? null : CellOf (character, source);
        var targetCell = character is null ? null : CellOf (character, target);
        string? refusal = null;
        if (sourceCell is null || targetCell is null)
        {
            refusal = "not both carried";
        }
        else if (source.Id == target.Id || source.GameId != target.GameId || source.Suffix != target.Suffix ||
                 source.WireObjectType != target.WireObjectType)
        {
            refusal = "not the same item";
        }

        var moved = amount <= 0 || amount > source.ItemCount ? source.ItemCount : amount;
        moved = Math.Min (moved, MaxStackSize - target.ItemCount);
        if (refusal is null && moved <= 0)
        {
            refusal = $"target holds {target.ItemCount} of {MaxStackSize}";
        }

        if (refusal is not null)
        {
            SphLogger.Info ($"Stack: {source.Id:X4} not merged into {target.Id:X4}: {refusal}. Client ID: {clientId:X4}");
            return false;
        }

        target.ItemCount += moved;
        source.ItemCount -= moved;
        DbConnection.Items.Update (target);
        SendCount (client, target, targetCell!.Value);

        if (source.ItemCount > 0)
        {
            DbConnection.Items.Update (source);
            SendCount (client, source, sourceCell!.Value);
        }
        else
        {
            Remove (client, character!, source, sourceCell!.Value);
        }

        client.SaveCharacter ();
        SphLogger.Info ($"Stack: {moved} moved from {source.Id:X4} ({source.ItemCount} left) into {target.Id:X4} " +
                        $"({target.ItemCount}). Client ID: {clientId:X4}");
        return true;
    }

    /// <summary>
    /// MainHand only references an inventory item, so it is never the item's own cell
    /// </summary>
    private static BelongingSlot? CellOf (CharacterDbEntry character, ItemDbEntry item) =>
        character.Items.Where (x => x.Value == item.Id && x.Key != BelongingSlot.MainHand)
            .Select (x => (BelongingSlot?) x.Key).FirstOrDefault ();

    private static void SendCount (SphereClient client, ItemDbEntry item, BelongingSlot cell)
    {
        client.MaybeQueueNetworkPacketSend (ItemRecordEncoder.EncodeCount (item));
        if (ItemSlotReserve.Build (client.CurrentCharacter!.ClientIndex, cell, item.Id, item.ItemCount) is { } reserve)
        {
            client.MaybeQueueNetworkPacketSend (reserve);
        }
    }

    /// <summary>
    /// The same way out as the last use of a consumable: every slot, the cell, the hand and the id
    /// </summary>
    private static void Remove (SphereClient client, CharacterDbEntry character, ItemDbEntry item, BelongingSlot cell)
    {
        var held = character.Items.TryGetValue (BelongingSlot.MainHand, out var heldId) && heldId == item.Id;
        foreach (var slot in character.Items.Where (x => x.Value == item.Id).Select (x => x.Key).ToList ())
        {
            character.Items.Remove (slot);
        }

        if (ItemSlotReserve.Build (character.ClientIndex, cell, ItemSlotReserve.NoItem) is { } clear)
        {
            client.MaybeQueueNetworkPacketSend (clear);
        }

        client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (client.GetLocalObjectId ((ushort) item.Id)));
        DbConnection.Items.Delete (item.Id);
        WorldObjectIndex.Release ((ushort) item.Id);

        if (held && character.RecalcCurrentStats ())
        {
            NetworkedStatsUpdater.Update (character);
        }
    }
}
