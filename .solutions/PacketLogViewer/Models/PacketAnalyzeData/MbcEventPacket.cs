using System;
using System.Collections.Generic;
using System.Linq;
using SpherePacketVisualEditor;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class MbcEventPacket : PacketAnalyzeData
{
    public string EventName { get; }
    public string Schema { get; }

    public override string DisplayValue
    {
        get
        {
            var id = Id == 0 ? "" : $"{Id:X} ";
            return $"{id}{EventName}";
        }
    }

    public MbcEventPacket(List<PacketPart> parts) : base(parts)
    {
        if (Id == 0)
        {
            Id = GetIntValue(PacketPartNames.ProcessId);
        }

        EventName = parts.FirstOrDefault(x => x.Name == PacketPartNames.ProcessId)?.Comment
                    ?? parts.FirstOrDefault(x => x.Name == PacketPartNames.WireRegion)?.Comment
                    ?? NamedCommand(parts)
                    ?? parts.FirstOrDefault(x => x.Name == PacketPartNames.ModuleTag)?.Comment
                    ?? "mbc";
        Schema = parts.FirstOrDefault(x => x.Name == PacketPartNames.WireRegion)?.EnumName ?? "";
    }

    private static string? NamedCommand(List<PacketPart> parts)
    {
        var command = parts.FirstOrDefault(x => x.Name == PacketPartNames.Command);
        if (command is null || string.IsNullOrEmpty(command.ListValuePrimary))
        {
            return null;
        }

        if (command.ListValuePrimary.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return command.ListValuePrimary;
    }
}
