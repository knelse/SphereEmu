namespace PacketLogViewer.Models.PacketAnalyzeData;

using System;
using System.Collections.Generic;
using System.Linq;
using SpherePacketVisualEditor;
using SphServer.Helpers;

public class TeleportWithTargetPacket : WorldObject
{
    public int SubtypeID { get; set; }
    public string OverrideType { get; set; }
    public bool HasTeleportTarget { get; set; }
    public string SubtypeStr => HasTeleportTarget ? "" : $" #{SubtypeID}";

    public override string DisplayValue =>
        $"{Id:X4} ({Enum.GetName(ObjectType) ?? string.Empty}){OverrideType}{SubtypeStr} at [{X:F2}, {Y:F2}, {Z:F2}]";

    public TeleportWithTargetPacket (List<PacketPart> parts) : base(parts)
    {
        if (ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
        {
            return;
        }

        SubtypeID = GetIntValue(PacketPartNames.SubtypeId);
        HasTeleportTarget = true;

        var keyLocales = SphObjectDb.LocalisationContent["_teleports"][Locale.Russian];
        var subtypeStr = $"0{SubtypeID}";
        var text = keyLocales.FirstOrDefault(x => x.StartsWith(subtypeStr));
        if (!string.IsNullOrEmpty(text))
        {
            OverrideType = " " + text[(subtypeStr.Length + 1)..];
        }
    }
}