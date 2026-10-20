using System;
using SphereHelpers.Extensions;
using Godot;
using SphServer.Client;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
/// Ground sack whose wire id is its container's DB id, so the open request names the container
/// directly
/// </summary>
public partial class LootBag : WorldObject
{
    /// <summary>
    /// Unclaimed loot goes away with its contents, as on live
    /// </summary>
    private const double LifetimeSeconds = 300;

    public ItemContainerDbEntry Container { get; private set; } = null!;

    private bool _despawned;

    public static LootBag Spawn (ItemContainerDbEntry container, Vector3 godotPosition)
    {
        var bag = new LootBag
        {
            Container = container,
            ID = (ushort) container.Id,
            ObjectType = ObjectType.Sack_Mob_Loot,
            Name = $"LootBag_{container.Id}",
            Angle = CreateRandomSpawnAngle ()
        };
        bag.Position = godotPosition;
        SphServer.Server.SphereServer.ServerNode.CallDeferred (Node.MethodName.AddChild, bag);
        return bag;
    }

    /// <summary>
    /// The id is the container's, already reserved and already in the item records
    /// </summary>
    protected override bool HoldsReservedId => true;

    public static LootBag? Find (int containerId) => ActiveWorldObjects.Get ((ushort) containerId) as LootBag;

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

    /// <summary>
    /// Bits after the 8-bit angle, identical in every live sack spawn (mobSpawnAndDrops)
    /// </summary>
    private const string LiveSpawnTail = "1001000100110100010011";

    /// <summary>
    /// Live sack spawns are 29 bytes: 22 after the header
    /// </summary>
    private const int LiveSpawnContentBits = 22 * 8;

    /// <summary>
    /// Built as live sends it; sack_mob_loot.spdp has a 9-bit angle and an extra properties
    /// record, and the client drew that sack but never filled it
    /// </summary>
    protected override void ShowForClient (SphereClient client)
    {
        if (_despawned)
        {
            return;
        }

        var origin = GlobalTransform.Origin;
        client.MaybeQueueNetworkPacketSend (BuildLiveSpawn (client.GetLocalObjectId (ID),
            origin.X, -origin.Y, -origin.Z, (byte) Angle));

        // This client opens the sack window locally, often without an open request, and shows
        // only what it already holds; items first, so the slot ids resolve when the list lands
        foreach (var packet in Container.GetContentsPackets ())
        {
            client.MaybeQueueNetworkPacketSend (packet);
        }
    }

    internal static byte[] BuildLiveSpawn (ushort entityId, float x, float y, float z, byte angle)
    {
        var stream = SphBitStream.GetWriteBitStream ();
        stream.WriteUInt16 (entityId, 16);
        stream.WriteByte (0, 2);
        stream.WriteUInt16 ((ushort) ObjectType.Sack_Mob_Loot, 10);
        stream.WriteByte (0, 1);
        stream.WriteByte (0x7C, 8);
        WriteFloat (stream, x);
        WriteFloat (stream, y);
        WriteFloat (stream, z);
        stream.WriteByte (angle, 8);

        var written = 16 + 2 + 10 + 1 + 8 + 3 * 32 + 8;
        foreach (var bit in LiveSpawnTail)
        {
            stream.WriteByte ((byte) (bit - '0'), 1);
            written++;
        }

        for (; written < LiveSpawnContentBits; written++)
        {
            stream.WriteByte (0, 1);
        }

        stream.WriteByte (0, 7);
        return Packet.ToByteArray (stream.GetStreamData (), 3);
    }

    /// <summary>
    /// Two 16-bit halves: a single 32-bit write drops a top bit of 1
    /// </summary>
    private static void WriteFloat (SphWriteStream stream, float value)
    {
        var bits = BitConverter.SingleToUInt32Bits (value);
        stream.WriteUInt16 ((ushort) bits, 16);
        stream.WriteUInt16 ((ushort) (bits >> 16), 16);
    }

    /// <summary>
    /// Last item taken: everyone who sees the sack loses it, not only the picker
    /// </summary>
    public void DespawnEmpty ()
    {
        Despawn ();
        ItemContainerDbEntry.Delete (Container, deleteItems: false);
    }

    private void Expire ()
    {
        if (_despawned)
        {
            return;
        }

        // Pickups update the stored container, not the copy this bag was spawned with
        var container = DbConnection.ItemContainers.FindById ((int) ID) ?? Container;
        SphLogger.Info ($"Loot bag {ID:X4} expired with {container.Contents.Count} item(s)");
        Despawn ();
        ItemContainerDbEntry.Delete (container, deleteItems: true);
    }

    private void Despawn ()
    {
        if (_despawned)
        {
            return;
        }

        _despawned = true;
        BroadcastDespawnToVisibleClients ();
        RemoveFromWorldRegistry ();
        WorldObjectIndex.Release (ID);
        QueueFree ();
    }
}
