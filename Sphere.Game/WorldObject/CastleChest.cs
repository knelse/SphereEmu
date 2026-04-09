using Godot;
using SphServer.Helpers;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleChest : WorldObject
{
    public CastleChest()
    {
        ObjectType = ObjectType.CastleChest;
        ModelName = "cs_chest";
    }

    protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
    {
        PacketPart.UpdateValue(packetParts, "object_type", (int)ObjectType, 10);

        return packetParts;
    }
}