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
    public readonly Dictionary<ushort, DateTime> DeleteForClients = new ();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready ()
    {
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process (double delta)
    {
        foreach (var clientInRange in MainServer.ActiveClients.Where(x =>
                     x.Value.IsReadyForGameLogic &&
                     x.Value.DistanceTo(GlobalTransform.Origin) <= 100))
        {
            if (!ShownForClients.ContainsKey(clientInRange.Key))
            {
                ShownForClients.Add(clientInRange.Key, DateTime.UtcNow);
                ShowForClient(clientInRange.Value);
            }

            if (DeleteForClients.ContainsKey(clientInRange.Key))
            {
                DeleteForClients.Remove(clientInRange.Key);
            }
        }

        foreach (var clientOutOfRange in MainServer.ActiveClients.Where(x =>
                     ShownForClients.ContainsKey(x.Key) && x.Value.DistanceTo(GlobalTransform.Origin) > 100))
        {
            if (DeleteForClients.ContainsKey(clientOutOfRange.Key) &&
                (DateTime.UtcNow - DeleteForClients[clientOutOfRange.Key]).Milliseconds > 100)
            {
                clientOutOfRange.Value.StreamPeer.PutData(CommonPackets.DespawnEntity(ID));
                ShownForClients.Remove(clientOutOfRange.Key);
                DeleteForClients.Remove(clientOutOfRange.Key);
            }
            else if (!DeleteForClients.ContainsKey(clientOutOfRange.Key))
            {
                DeleteForClients.Add(clientOutOfRange.Key, DateTime.UtcNow);
            }
        }
    }

    public void ShowForClient (Client client)
    {
        var packetParts = GetPacketPartsAndUpdateCoordsAndID(client);
        packetParts = ModifyPacketParts(packetParts);
        var npcPacket = PacketPart.GetBytesToWrite(packetParts);
        client.StreamPeer.PutData(npcPacket);
    }

    public virtual List<PacketPart> GetPacketParts ()
    {
        return PacketPart.LoadDefinedPartsFromFile(ObjectType);
    }

    public List<PacketPart> GetPacketPartsAndUpdateCoordsAndID (Client client)
    {
        var packetParts = GetPacketParts();
        PacketPart.UpdateCoordinates(packetParts, GlobalTransform.Origin.X, GlobalTransform.Origin.Y,
            GlobalTransform.Origin.Z, Angle);
        var localId = client.GetLocalObjectId(ID);
        PacketPart.UpdateEntityId(packetParts, localId);

        return packetParts;
    }

    public virtual List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        return packetParts;
    }
}