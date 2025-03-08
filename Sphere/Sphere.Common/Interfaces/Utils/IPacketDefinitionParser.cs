using Sphere.Common.Packets;

namespace Sphere.Common.Interfaces.Utils
{
    public interface IPacketDefinitionParser
    {
        Dictionary<string, PacketDefinition> Load();
    }
}
