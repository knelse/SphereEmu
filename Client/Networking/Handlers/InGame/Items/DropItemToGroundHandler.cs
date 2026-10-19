using System.Linq;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

/// <summary>
/// 08 40 63. The carried item id is a guid at bit 141
/// </summary>
public class DropItemToGroundHandler (Guid entityId, ClientConnection clientConnection, SphereClient client)
    : ISphereClientNetworkingHandler
{
    private const int ItemIdBitOffset = 141;

    public async Task Handle (byte[] frame, double delta)
    {
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (entityId));

        var stream = new BitStream (frame);
        stream.ReadBits (ItemIdBitOffset);
        var itemId = stream.ReadGuid ();

        var character = client.CurrentCharacter;
        if (character is null)
        {
            return;
        }

        var slots = character.Items.Where (kv => kv.Value == itemId).Select (kv => kv.Key).ToList ();
        var item = DbConnection.Items.FindById (itemId);
        if (slots.Count == 0 || item is null)
        {
            SphLogger.Warning ($"Drop to ground: item {itemId} is not carried. Client ID: {entityId}");
            return;
        }

        var wasWorn = slots.Any (slot => !ItemDbEntry.IsInventorySlot (slot));
        foreach (var slot in slots)
        {
            character.Items.Remove (slot);
        }

        if (wasWorn && character.RecalcCurrentStats ())
        {
            NetworkedStatsUpdater.Update (character);
        }

        item.ParentContainerId = null;
        item.X = character.X;
        item.Y = character.Y;
        item.Z = character.Z;
        DbConnection.Items.Update (item);
        client.SaveCharacter ();

        foreach (var slot in slots)
        {
            if (ItemSlotReserve.Build (character.EntityId, slot, ItemSlotReserve.NoItem) is { } clear)
            {
                clientConnection.MaybeScheduleNetworkPacketSend (clear);
            }
        }

        clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.Encode (
            item, item.Id, (float) item.X, (float) -item.Y, (float) -item.Z));

        var name = item.Localization.GetValueOrDefault (Locale.Russian, item.GameObjectType.ToString ());
        SphLogger.Info ($"Dropped {name} [{itemId}] from {string.Join (",", slots)}. Client ID: {entityId}");
        await Task.CompletedTask;
    }
}
