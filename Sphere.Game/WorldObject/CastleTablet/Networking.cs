using SphServer.Helpers;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

public partial class CastleTablet
{
	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "object_type", (int)ObjectType, 10);
		PacketPart.UpdateValue(packetParts, "castle_id", (int)Castle, 6);
		PacketPart.UpdateValue(packetParts, "clan_name", ClanName, true, 8);

		return packetParts;
	}
}
