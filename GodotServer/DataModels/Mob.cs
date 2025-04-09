using System.Text;
using Godot;
using SphServer;
using SphServer.Helpers;

public class Mob
{
    private static readonly PackedScene MobScene = (PackedScene) ResourceLoader.Load("res://Mob.tscn");
    public int Id { get; set; }

    public ushort TypeID { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Angle { get; set; }

    public SphMonsterInstance Monster { get; set; }

    public ulong? ParentNodeId { get; set; }

    public void ShowForEveryClientInRadius ()
    {
        foreach (var client in MainServer.ActiveClients.Values)
        {
            // TODO: proper load/unload for client
            // && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
            // MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
            // client.StreamPeer.PutData(Packet.ToByteArray(ToByteArray(client.LocalId), 1));
        }
    }
}