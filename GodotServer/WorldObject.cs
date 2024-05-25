using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer;
using SphServer.Helpers;
using SphServer.Packets;

public partial class WorldObject : Node3D
{
    [Export] public int Angle { get; set; }
    [Export] public ushort ID { get; set; } = 0x1234;

    [Export] public ObjectType ObjectType { get; set; } = ObjectType.Unknown;

    public readonly Dictionary<ushort, DateTime> ShownForClients = new ();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready ()
    {
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process (double delta)
    {
        foreach (var clientInRange in MainServer.ActiveClients.Where(x =>
                     x.Value.IsReadyForGameLogic && !ShownForClients.ContainsKey(x.Key) &&
                     x.Value.DistanceTo(GlobalTransform.Origin) <= 100))
        {
            ShownForClients.Add(clientInRange.Key, DateTime.UtcNow);
            ShowForClient(clientInRange.Value);
        }

        foreach (var clientOutOfRange in MainServer.ActiveClients.Where(x =>
                     ShownForClients.ContainsKey(x.Key) && x.Value.DistanceTo(GlobalTransform.Origin) > 100))
        {
            clientOutOfRange.Value.StreamPeer.PutData(CommonPackets.DespawnEntity(ID));
            ShownForClients.Remove(clientOutOfRange.Key);
        }
    }

    public void ShowForClient (Client client)
    {
        var packetParts = PacketPart.LoadDefinedPartsFromFile(ObjectType);
        PacketPart.UpdateCoordinates(packetParts, GlobalTransform.Origin.X, GlobalTransform.Origin.Y,
            GlobalTransform.Origin.Z, Angle);
        var localId = client.GetLocalObjectId(ID);
        PacketPart.UpdateEntityId(packetParts, localId);
        var npcPacket = PacketPart.GetBytesToWrite(packetParts);
        client.StreamPeer.PutData(npcPacket);
    }
}