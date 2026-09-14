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
            return string.IsNullOrEmpty(Schema) ? $"{id}{EventName}" : $"{id}{EventName} ({Schema})";
        }
    }

    public MbcEventPacket(List<PacketPart> parts) : base(parts)
    {
        if (Id == 0)
        {
            Id = GetIntValue(PacketPartNames.ProcessId);
        }

        EventName = parts.FirstOrDefault(x => x.Name == PacketPartNames.WireRegion)?.Comment
                    ?? parts.FirstOrDefault(x => x.Name == PacketPartNames.ModuleTag)?.Comment
                    ?? parts.FirstOrDefault(x => x.Name == PacketPartNames.ProcessId)?.Comment
                    ?? "mbc";
        Schema = parts.FirstOrDefault(x => x.Name == PacketPartNames.WireRegion)?.EnumName ?? "";
    }
}
