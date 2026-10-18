using System.Linq;
using SphereHelpers.Extensions;
using SphServer.Shared.BitStream;
using Godot;
using SphServer.Client;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
/// An item a player dropped, lying in the world as itself; its wire id is the item's id
/// </summary>
public partial class GroundItem : WorldObject
{
    /// <summary>
    /// What nobody picked up goes back to the player who dropped it
    /// </summary>
    private const double LifetimeSeconds = 300;

    public ItemDbEntry Item { get; private set; } = null!;
    public int DroppedByCharacterId { get; private set; }

    private bool _gone;

    public static GroundItem Spawn (ItemDbEntry item, int droppedByCharacterId, Vector3 godotPosition)
    {
        var groundItem = new GroundItem
        {
            Item = item,
            DroppedByCharacterId = droppedByCharacterId,
            ID = (ushort) item.Id,
            Name = $"GroundItem_{item.Id}",
            Angle = CreateRandomSpawnAngle ()
        };
        groundItem.Position = godotPosition;
        SphServer.Server.SphereServer.ServerNode.CallDeferred (Node.MethodName.AddChild, groundItem);
        return groundItem;
    }

    public static GroundItem? Find (int itemId) => ActiveWorldObjects.Get ((ushort) itemId) as GroundItem;

    /// <summary>
    /// The item id is already reserved and already the client's entity
    /// </summary>
    protected override bool HoldsReservedId => true;

    public override void _Ready ()
    {
        base._Ready ();
        if (Engine.IsEditorHint ())
        {
            return;
        }

        GetTree ().CreateTimer (LifetimeSeconds).Timeout += Expire;
        EnsureVisibilityArea ();
    }

    protected override void ShowForClient (SphereClient client)
    {
        if (_gone)
        {
            return;
        }

        var origin = GlobalTransform.Origin;
        client.MaybeQueueNetworkPacketSend (
            ItemRecordEncoder.EncodeOnGround (Item, origin.X, -origin.Y, -origin.Z, (byte) Angle));
    }

    /// <summary>
    /// Picked up: the picker already has it in a slot, everyone else loses it from the ground.
    /// The picker's entity is now the inventory item, so it gets no despawn
    /// </summary>
    public void PickedUp (SphereClient picker)
    {
        if (_gone)
        {
            return;
        }

        SphLogger.Info ($"Ground item {ID:X4} picked up");
        ForgetVisibleClient (picker);
        Remove ();
    }

    private void Expire ()
    {
        if (_gone)
        {
            return;
        }

        var client = ActiveClients.GetAll ().Values
            .FirstOrDefault (x => x.CurrentCharacter?.Id == DroppedByCharacterId);
        var owner = client?.CurrentCharacter ?? DbConnection.Characters.FindById (DroppedByCharacterId);
        if (owner?.FindEmptyInventorySlot () is null)
        {
            GetTree ().CreateTimer (30).Timeout += Expire;
            return;
        }

        Remove ();
        var slot = ReturnItemToOwner (Item);
        SphLogger.Info ($"Ground item {ID:X4} expired, returned to owner {DroppedByCharacterId} in {slot?.ToString () ?? "no free cell"}");
    }

    public static void ReturnGroundItemsFromLastRun ()
    {
        foreach (var item in DbConnection.Items.Find (x => x.DroppedOnGroundBy != null).ToList ())
        {
            ReturnItemToOwner (item);
        }
    }

    private static BelongingSlot? ReturnItemToOwner (ItemDbEntry item)
    {
        if (item.DroppedOnGroundBy is not { } ownerId)
        {
            return null;
        }

        var client = ActiveClients.GetAll ().Values.FirstOrDefault (x => x.CurrentCharacter?.Id == ownerId);
        var owner = client?.CurrentCharacter ?? DbConnection.Characters.FindById (ownerId);
        if (owner?.FindEmptyInventorySlot () is not { } slot)
        {
            return null;
        }

        owner.Items[slot] = item.Id;
        DbConnection.Characters.Update (owner);
        item.ParentContainerId = null;
        item.DroppedOnGroundBy = null;
        DbConnection.Items.Update (item);
        if (client is not null)
        {
            client.MaybeQueueNetworkPacketSend (ItemRecordEncoder.Encode (item,
                SphBitStream.ByteSwap (owner.ClientIndex)));
            if (ItemSlotReserve.Build (owner.ClientIndex, slot, item.Id, item.ItemCount) is { } reserve)
            {
                client.MaybeQueueNetworkPacketSend (reserve);
            }
        }

        return slot;
    }

    private void Remove ()
    {
        _gone = true;
        BroadcastDespawnToVisibleClients ();
        StopVisibility ();
        RemoveFromWorldRegistry ();
        QueueFree ();
    }
}
