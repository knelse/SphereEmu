using Godot;
using SphServer.Helpers;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleEntrance : WorldObject
{
	[Export] public Castles Castle { get; set; }

	public CastleEntrance()
	{
		ObjectType = ObjectType.CastleEntrance;
		ModelName = "EDOOR";
	}

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "object_type", (int)ObjectType, 10);
		PacketPart.UpdateValue(packetParts, "castle_id", (int)(Castle + 56), 6);

		return packetParts;
	}
}
