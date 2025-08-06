using System.Collections.Generic;
using Godot;
using SphServer.Packets;

namespace SphServer;

public partial class TeleportWithTarget : WorldObject
{
    [Export] public int SubtypeID { get; set; }

    public override List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        PacketPart.UpdateValue(packetParts, "subtype_id", SubtypeID, 16);
        PacketPart.UpdateValue(packetParts, "subtype_plus_1000", SubtypeID + 1000, 18);

        return packetParts;
    }
}