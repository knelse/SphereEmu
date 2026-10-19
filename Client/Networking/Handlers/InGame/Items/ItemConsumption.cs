using System.Linq;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

/// <summary>
/// Item scripts wait for the server's count message, or for the item to go away
/// </summary>
public static class ItemConsumption
{
    /// <summary>
    /// One use of a carried item. Call once per use, not per target
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
                ItemSlotReserve.Build (character.EntityId, stackCell, item.Id, item.ItemCount) is { } reserve)
            {
                client.MaybeQueueNetworkPacketSend (reserve);
            }

            SphLogger.Info ($"Used one {name} [{item.Id}], {item.ItemCount} left. Client ID: {character.EntityId}");
            return;
        }

        var held = character.Items.TryGetValue (BelongingSlot.MainHand, out var heldId) && heldId == item.Id;
        foreach (var slot in character.Items.Where (x => x.Value == item.Id).Select (x => x.Key).ToList ())
        {
            character.Items.Remove (slot);
        }

        if (cell is { } lastCell &&
            ItemSlotReserve.Build (character.EntityId, lastCell, ItemSlotReserve.NoItem) is { } clear)
        {
            client.MaybeQueueNetworkPacketSend (clear);
        }

        client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (item.Id));
        DbConnection.Items.Delete (item.Id);

        if (held && character.RecalcCurrentStats ())
        {
            NetworkedStatsUpdater.Update (character);
        }

        client.SaveCharacter ();
        SphLogger.Info ($"Used the last {name} [{item.Id}], removed. Client ID: {character.EntityId}");
    }
}
