using System;
using System.Collections.Generic;
using System.Linq;
using SphereHelpers.Extensions;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class NpcTradePacket : PacketAnalyzeData
{
    public EntityActionType ActionType { get; set; } = EntityActionType.UNDEF;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int Angle { get; set; }
    public string Name { get; set; } = string.Empty;
    public int NameId { get; set; }
    public int TypeNameLength { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int IconNameLength { get; set; }
    public string IconName { get; set; } = string.Empty;
    public int NpcTradeType { get; set; }

    public override string DisplayValue =>
        $"{Id:X4} ({Enum.GetName(ObjectType) ?? string.Empty}) {Name} ({NpcTradeType}, {TypeName}, {IconName}) at [{X:F2}, {Y:F2}, {Z:F2}]";

    public NpcTradePacket (List<PacketPart> parts) : base(parts)
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
            var nameVal = GetIntValue(PacketPartNames.NameID);
            NameId = nameVal;
            Name = PacketLogViewerMainWindow.DefinedEnums["npc_names"].TryGetValue(nameVal, out var name)
                ? name
                : string.Empty;
            TypeNameLength = GetIntValue(PacketPartNames.TypeNameLength);
            TypeName = GetStringValue(PacketPartNames.TypeName);
            IconNameLength = GetIntValue(PacketPartNames.IconNameLength);
            IconName = GetStringValue(PacketPartNames.IconName);
            NpcTradeType = GetIntValue(PacketPartNames.NpcTradeType);
        }
    }
}