using System.Collections.Generic;
using System.Linq;
using BitStreams;
using Godot;
using SphServer.Helpers;
using SphServer.Packets;

namespace SphServer;

public partial class Door : WorldObject
{
    [Export] public int DoorID { get; set; }
    [Export] public bool HasTarget { get; set; }
    [Export] public double TargetX { get; set; }
    [Export] public double TargetY { get; set; }
    [Export] public double TargetZ { get; set; }

    public override List<PacketPart> GetPacketParts ()
    {
        return HasTarget ? PacketPart.LoadDefinedWithOverride("door_entrance_tp") : base.GetPacketParts();
    }

    public override List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        PacketPart.UpdateValue(packetParts, "door_id", DoorID, 7);
        if (HasTarget)
        {
            var xValueBytes = CoordsHelper.EncodeServerCoordinate(TargetX);
            var xValue = new BitStream(xValueBytes).ReadBits(int.MaxValue).ToList();
            var yValueBytes = CoordsHelper.EncodeServerCoordinate(TargetY);
            var yValue = new BitStream(yValueBytes).ReadBits(int.MaxValue).ToList();
            var zValueBytes = CoordsHelper.EncodeServerCoordinate(TargetZ);
            var zValue = new BitStream(zValueBytes).ReadBits(int.MaxValue).ToList();
            foreach (var part in packetParts)
            {
                part.Value = part.Name switch
                {
                    "target_x" => xValue,
                    "target_y" => yValue,
                    "target_z" => zValue,
                    _ => part.Value
                };
            }
        }

        return packetParts;
    }
}