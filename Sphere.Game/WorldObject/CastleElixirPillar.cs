using Godot;
using SphServer.Helpers;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleElixirPillar : WorldObject
{
	public CastleElixirPillar()
	{
		ObjectType = ObjectType.CastleElixirPillar;
		ModelName = "cs_knot";
	}

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "object_type", (int)ObjectType, 10);

		return packetParts;
	}
}
