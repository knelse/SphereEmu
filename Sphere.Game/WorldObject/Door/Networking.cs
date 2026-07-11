using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Door
{
	protected override List<PacketPart> GetPacketParts()
	{
		return HasTarget ? PacketPart.LoadDefinedWithOverride("door_entrance_tp") : base.GetPacketParts();
	}

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "door_id", DoorID, 7);
		if (HasTarget)
		{
			PacketPart.UpdateTargetCoordinates(packetParts, TargetX, TargetY, TargetZ);
		}

		return packetParts;
	}
}
