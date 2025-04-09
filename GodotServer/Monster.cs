using System.Collections.Generic;
using Godot;
using SphServer.Enums;
using SphServer.Packets;

namespace SphServer;

public partial class Monster : WorldObject
{
    [Export] public MonsterType MonsterType { get; set; }
    [Export] public bool HasName { get; set; }
    [Export] public int NameID_1 { get; set; }
    [Export] public int NameID_2 { get; set; }
    [Export] public int NameID_3 { get; set; }
    [Export] public int CurrentHP { get; set; }
    [Export] public int MaxHP { get; set; }
    [Export] public int Level { get; set; }

    public override List<PacketPart> GetPacketParts ()
    {
        // TODO named and higher levels
        return HasName ? base.GetPacketParts() :
            Level == 0 ? PacketPart.LoadDefinedWithOverride("monster_level_1") :
            PacketPart.LoadDefinedWithOverride("monster_level_1");
    }

    public override List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        var hpSize = MaxHP >= 128 ? 16 : 8;
        PacketPart.UpdateValue(packetParts, "current_hp", CurrentHP, hpSize);
        PacketPart.UpdateValue(packetParts, "max_hp", MaxHP, hpSize);
        if (hpSize == 16)
        {
            PacketPart.UpdateValue(packetParts, "hp_size_t", 17, 5);
            PacketPart.UpdateValue(packetParts, "skip_1", 1, 1);
            PacketPart.UpdateValue(packetParts, "skip_2", 1, 1);
        }

        var mobTypeId = MonsterTypeMapping.MonsterNameToMonsterTypeMapping[MonsterType];
        PacketPart.UpdateValue(packetParts, "mob_type", mobTypeId, 14);
        // TODO level without these
        if (Level > 0)
        {
            // var levelValue = (0b0001000 << 22) + (((Level >> 5) & 0b11111) << 17) + (0b101001111101 << 5) +
            //                  (Level & 0b11111);
            // PacketPart.UpdateValue(packetParts, "level", levelValue, 29);
            PacketPart.UpdateValue(packetParts, "level_last_3", Level & 0b111, 3);
        }

        if (HasName)
        {
        }

        return packetParts;
    }
}