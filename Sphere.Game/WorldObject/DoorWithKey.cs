using Godot;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class DoorWithKey : WorldObject
{
	[Export] public int SubtypeID { get; set; }

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "subtype_id", SubtypeID, 15);
		return packetParts;
	}
}
