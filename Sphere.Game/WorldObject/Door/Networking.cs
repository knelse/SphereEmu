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
		// door_entrance*.spdp uses subtype_id (15 bits); "door_id"/7 was a no-op.
		packetParts = base.ModifyPacketParts(packetParts);
		PacketPart.UpdateValue(packetParts, "subtype_id", DoorID, 15);
		if (HasTarget)
		{
			PacketPart.UpdateTargetCoordinates(packetParts, TargetX, TargetY, TargetZ);
		}

		return packetParts;
	}
}
