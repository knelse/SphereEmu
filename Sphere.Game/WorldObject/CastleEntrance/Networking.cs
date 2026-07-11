using SphServer.Helpers;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

public partial class CastleEntrance
{
	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "object_type", (int)ObjectType, 10);
		PacketPart.UpdateValue(packetParts, "castle_id", (int)(Castle + 56), 7);

		return packetParts;
	}
}
