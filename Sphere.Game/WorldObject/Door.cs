using Godot;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class Door : WorldObject
{
	[Export] public int DoorID { get; set; }
	[Export] public bool HasTarget { get; set; }
	[Export] public double TargetX { get; set; }
	[Export] public double TargetY { get; set; }
	[Export] public double TargetZ { get; set; }

	protected override List<PacketPart> GetPacketParts ()
	{
		return HasTarget ? PacketPart.LoadDefinedWithOverride("door_entrance_tp") : base.GetPacketParts();
	}

	protected override List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "door_id", DoorID, 7);
		if (HasTarget)
		{
			PacketPart.UpdateTargetCoordinates(packetParts, TargetX, TargetY, TargetZ);
		}

		return packetParts;
	}
}
