using System.Linq;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

/// <summary>
/// Item scripts never lower their own amount on use: they send cuse and wait for the server's
/// count message, or for the item to go away
/// </summary>
public static class ItemConsumption
{
    /// <summary>
    /// One use of a carried item: the amount drops by one, and the last one leaves the bag cell,
    /// the hand and the database. Call once per use, not per target
    /// </summary>
    public static void ConsumeOne (SphereClient client, ItemDbEntry item)
    {
        var character = client.CurrentCharacter;
        if (character is null || !character.Items.ContainsValue (item.Id) ||
            DbConnection.Items.FindById (item.Id) is not { } storedItem)
        {
            return;
        }
        item = storedItem;

        var name = item.Localization.GetValueOrDefault (Locale.Russian, "?");
        var cell = character.Items.Where (x => x.Value == item.Id && x.Key != BelongingSlot.MainHand)
            .Select (x => (BelongingSlot?) x.Key).FirstOrDefault ();

        if (item.ItemCount > 1)
        {
            item.ItemCount--;
            DbConnection.Items.Update (item);
            client.MaybeQueueNetworkPacketSend (ItemRecordEncoder.EncodeCount (item));
            if (cell is { } stackCell &&
                ItemSlotReserve.Build (character.ClientIndex, stackCell, item.Id, item.ItemCount) is { } reserve)
            {
                client.MaybeQueueNetworkPacketSend (reserve);
            }

            SphLogger.Info ($"Used one {name} [{item.Id:X4}], {item.ItemCount} left. Client ID: {character.ClientIndex:X4}");
            return;
        }

        var held = character.Items.TryGetValue (BelongingSlot.MainHand, out var heldId) && heldId == item.Id;
        foreach (var slot in character.Items.Where (x => x.Value == item.Id).Select (x => x.Key).ToList ())
        {
            character.Items.Remove (slot);
        }

        if (cell is { } lastCell &&
            ItemSlotReserve.Build (character.ClientIndex, lastCell, ItemSlotReserve.NoItem) is { } clear)
        {
            client.MaybeQueueNetworkPacketSend (clear);
        }

        client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (client.GetLocalObjectId ((ushort) item.Id)));
        DbConnection.Items.Delete (item.Id);
        WorldObjectIndex.Release ((ushort) item.Id);

        // An emptied hand changes the hand attack values
        if (held && character.RecalcCurrentStats ())
        {
            NetworkedStatsUpdater.Update (character);
        }

        client.SaveCharacter ();
        SphLogger.Info ($"Used the last {name} [{item.Id:X4}], removed. Client ID: {character.ClientIndex:X4}");
    }
}
