using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Packets;
using SphServer.Shared.WorldState;

public enum ClientInteractionType
{
    OpenTrade,
    Buy,
    Sell,
    ChangeHealth,
    TakeMission,
    MoveInWorld,
    MoveInInventory,
    OpenContainer,
    PickupToSlot,
    Pickup,
    Drop,
    Use,
    EquipToMainhand,
    EquipToCharacter,
    Unknown
}

public partial class WorldObject : Node3D
{
    [Export] public int Angle { get; set; }
    [Export] public ushort ID { get; set; }
    [Export] public ObjectType ObjectType { get; set; } = ObjectType.Unknown;

    public readonly Dictionary<ushort, DateTime> ShownForClients = new ();
    public readonly Dictionary<ushort, DateTime> DeleteForClients = new ();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready ()
    {
        if (ID == 0)
        {
            ID = WorldObjectIndex.New();
        }
        
        ActiveNodes.Add(GetInstanceId(), this);
        //ActiveWorldObjectRepository.Add(ID, this);
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process (double delta)
    {
        // foreach (var clientInRange in SphereServer.ActiveClients.Where(x =>
        //              x.Value.IsReadyForGameLogic &&
        //              x.Value.DistanceTo(GlobalTransform.Origin) <= SphereServer.CLIENT_OBJECT_VISIBILITY_DISTANCE))
        // {
        //     if (!ShownForClients.ContainsKey(clientInRange.Key))
        //     {
        //         ShownForClients.Add(clientInRange.Key, DateTime.UtcNow);
        //         ShowForClient(clientInRange.Value);
        //     }
        //
        //     if (DeleteForClients.ContainsKey(clientInRange.Key))
        //     {
        //         DeleteForClients.Remove(clientInRange.Key);
        //     }
        // }
        //
        // foreach (var clientOutOfRange in SphereServer.ActiveClients.Where(x =>
        //              ShownForClients.ContainsKey(x.Key) && x.Value.DistanceTo(GlobalTransform.Origin) >
        //              SphereServer.CLIENT_OBJECT_VISIBILITY_DISTANCE))
        // {
        //     if (DeleteForClients.TryGetValue(clientOutOfRange.Key, out var value) &&
        //         (DateTime.UtcNow - value).Milliseconds > 500)
        //     {
        //         clientOutOfRange.Value.StreamPeer.PutData(CommonPackets.DespawnEntity(ID));
        //         ShownForClients.Remove(clientOutOfRange.Key);
        //         DeleteForClients.Remove(clientOutOfRange.Key);
        //     }
        //     else if (!DeleteForClients.ContainsKey(clientOutOfRange.Key))
        //     {
        //         DeleteForClients.Add(clientOutOfRange.Key, DateTime.UtcNow);
        //     }
        // }
    }

    public void ShowForClient (SphereClient client)
    {
        // var packetParts = GetPacketPartsAndUpdateCoordsAndID(client);
        // packetParts = ModifyPacketParts(packetParts);
        // var packet = PostprocesPacketBytes(PacketPart.GetBytesToWrite(packetParts));
        // client.StreamPeer.PutData(packet);
    }

    public virtual List<PacketPart> GetPacketParts ()
    {
        return PacketPart.LoadDefinedPartsFromFile(ObjectType);
    }

    public List<PacketPart> GetPacketPartsAndUpdateCoordsAndID (SphereClient client)
    {
        var packetParts = GetPacketParts();
        // PacketPart.UpdateCoordinates(packetParts, GlobalTransform.Origin.X, GlobalTransform.Origin.Y,
        //     GlobalTransform.Origin.Z, Angle);
        // var localId = client.GetLocalObjectId(ID);
        // PacketPart.UpdateEntityId(packetParts, localId);

        return packetParts;
    }

    public virtual List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        return packetParts;
    }

    public virtual byte[] PostprocesPacketBytes (byte[] packet)
    {
        return packet;
    }

    public virtual void ClientInteract (ushort clientID,
        ClientInteractionType interactionType = ClientInteractionType.Unknown)
    {
        Console.WriteLine($"Client [{clientID}] interacts with [{ID}] {ObjectType} -- {interactionType}");
    }
}