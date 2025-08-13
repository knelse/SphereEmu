using System.Collections.Generic;
using Godot;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

public partial class AlchemyResource : WorldObject
{
    [Export] public int GameObjectID { get; set; }

    protected override List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        // PacketPart.UpdateValue(packetParts, "__hasGameId", 1, 1);
        PacketPart.UpdateValue(packetParts, "game_object_id", GameObjectID, 14);
        // here we spawn one of 3 types (plant, metal, mineral), so it's necessary
        PacketPart.UpdateValue(packetParts, "object_type", (int) ObjectType, 10);
        // 0xFF00 = on the ground
        // PacketPart.UpdateValue(packetParts, "container_id", 0xFF00, 16);
        // PacketPart.UpdateValue(packetParts, "count", 1, 15);

        return packetParts;
    }
}