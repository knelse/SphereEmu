using Godot;
using SphServer.Helpers;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleTablet : WorldObject
{
    [Export] public Castles Castle { get; set; }
    [Export] public string ClanName { get; set; }

    public CastleTablet()
    {
        ObjectType = ObjectType.CastleTablet;
        ModelName = "cs_table";
        ClanName = "Зеленый Слоник";
    }

    protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
    {
        PacketPart.UpdateValue(packetParts, "object_type", (int)ObjectType, 10);
        PacketPart.UpdateValue(packetParts, "castle_id", (int)(Castle + 8), 6);
        PacketPart.UpdateValue(packetParts, "clan_name", ClanName, true, 8);

        return packetParts;
    }
}