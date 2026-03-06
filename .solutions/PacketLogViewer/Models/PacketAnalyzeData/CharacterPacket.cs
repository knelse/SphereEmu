using System;
using System.Collections.Generic;
using System.Linq;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class CharacterPacket : PacketAnalyzeData
{
    public EntityActionType ActionType { get; set; } = EntityActionType.UNDEF;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int Angle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ClanName = string.Empty;
    public ClanRank ClanRank = ClanRank.Neophyte;
    public int TitleLevel = 0;
    public int DegreeLevel = 0;
    public Guild Guild = Guild.None;
    public int GuildLevel = 0;

    public override string DisplayValue =>
        $"{Id:X4} (Player) {Name} [{ClanName}] ({ClanRank}), "
        + $"{TitleLevel} / {DegreeLevel}, {Guild} ({GuildLevel}) at [{X:F2}, {Y:F2}, {Z:F2}]";

    public CharacterPacket (List<PacketPart> parts) : base(parts)
    {
        var actionTypePart = Parts.FirstOrDefault(x => x.Name == PacketPartNames.ActionType);
        if (actionTypePart is not null)
        {
            var actionTypeVal = (int) (actionTypePart.ActualLongValue ?? int.MaxValue);
            ActionType = Enum.IsDefined(typeof (EntityActionType), actionTypeVal)
                ? (EntityActionType) actionTypeVal
                : EntityActionType.UNDEF;
        }

        if (ActionType is EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN)
        {
            X = GetClientCoordValue(PacketPartNames.CoordX);
            Y = GetClientCoordValue(PacketPartNames.CoordY);
            Z = GetClientCoordValue(PacketPartNames.CoordZ);
            Angle = GetIntValue(PacketPartNames.Angle);
        }

        if (ActionType is EntityActionType.FULL_SPAWN)
        {
            Name = GetStringValue(PacketPartNames.CharacterName);
            ClanName = GetStringValue(PacketPartNames.ClanName);
            // TypeNameLength = GetIntValue(PacketPartNames.TypeNameLength);
            // TypeName = GetStringValue(PacketPartNames.TypeName);
            // IconNameLength = GetIntValue(PacketPartNames.IconNameLength);
            // IconName = GetStringValue(PacketPartNames.IconName);
            // NpcTradeType = GetIntValue(PacketPartNames.NpcTradeType);
        }
    }
}