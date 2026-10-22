using System;
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
using SphServer.Sphere.Game.WorldObject;
using Vector3 = Godot.Vector3;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

// 08 40 63 at bytes 13-15, length 0x2D; the rest is an empty item record template with no position
public class DropItemToGroundHandler (ushort localId, ClientConnection clientConnection, SphereClient client)
    : ISphereClientNetworkingHandler
{
    private const int ItemIdBitOffset = 141;
    private const float MaxDragDistance = 10f;

    public async Task Handle (byte[] frame, double delta)
    {
        // Same use timeout as the drag, or the client stays wedged
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));

        var stream = new BitStream (frame);
        stream.ReadBits (ItemIdBitOffset);
        var itemId = (int) stream.ReadUInt32 (16);

        var character = client.CurrentCharacter;
        if (character is null)
        {
            return;
        }

        var slots = character.Items.Where (kv => kv.Value == itemId).Select (kv => kv.Key).ToList ();
        var item = DbConnection.Items.FindById (itemId);
        if (slots.Count == 0 || item is null)
        {
            SphLogger.Warning ($"Drop to ground: item {itemId:X4} is not carried. Client ID: {localId:X4}");
            return;
        }

        // Ground items carry no cells, so a backpack goes down only empty
        if (Backpack.IsBackpack (item) && item.BackpackContents.Count > 0)
        {
            foreach (var slot in slots)
            {
                if (ItemSlotReserve.Build (localId, slot, item.Id, item.ItemCount) is { } kept)
                {
                    clientConnection.MaybeScheduleNetworkPacketSend (kept);
                }
            }

            SphLogger.Info ($"Drop to ground: backpack {itemId:X4} still holds {item.BackpackContents.Count} " +
                            $"item(s). Client ID: {localId:X4}");
            return;
        }

        // Live keeps a dropped item in the world as itself, sent with the ground placement
        var wasWorn = slots.Any (slot => !ItemDbEntry.IsInventorySlot (slot));
        foreach (var slot in slots)
        {
            character.Items.Remove (slot);
        }

        if (wasWorn && character.RecalcCurrentStats ())
        {
            NetworkedStatsUpdater.Update (character);
        }

        client.SaveCharacter ();

        // The drag sends the item's position as it moves; a stale one from an old drag is ignored
        var dragged = new Vector3 ((float) item.X, (float) item.Y, (float) item.Z);
        var position = dragged.DistanceTo (character.Origin) <= MaxDragDistance ? dragged : character.Origin;

        item.ParentContainerId = null;
        item.DroppedOnGroundBy = character.Id;
        item.X = position.X;
        item.Y = position.Y;
        item.Z = position.Z;
        DbConnection.Items.Update (item);

        // The bag window draws the player's slot array, so the cells are cleared as UseItem does;
        // the item itself is already the client's, so it is moved rather than spawned again
        foreach (var slot in slots)
        {
            if (ItemSlotReserve.Build (localId, slot, ItemSlotReserve.NoItem) is { } clear)
            {
                clientConnection.MaybeScheduleNetworkPacketSend (clear);
            }
        }

        clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.EncodeDropToGround (item,
            position.X, -position.Y, -position.Z, (byte) GroundItem.CreateRandomSpawnAngle ()));
        GroundItem.Spawn (item, character.Id, position);

        var name = item.Localization.GetValueOrDefault (Locale.Russian, item.GameObjectType.ToString ());
        SphLogger.Info ($"Dropped {name} [{itemId:X4}] from {string.Join (",", slots)} onto the ground at " +
                        $"({position.X:F1}, {position.Y:F1}, {position.Z:F1}). Client ID: {localId:X4}");
    }
}
