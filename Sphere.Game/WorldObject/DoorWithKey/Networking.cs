using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

public partial class DoorWithKey
{
	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "subtype_id", SubtypeID, 15);
		return packetParts;
	}
}
