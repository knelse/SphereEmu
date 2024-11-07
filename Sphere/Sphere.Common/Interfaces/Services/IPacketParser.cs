using Sphere.Common.Interfaces.Packets;

namespace Sphere.Common.Interfaces.Services
{
    public interface IPacketParser
    {
        IPacket? Parse(PacketBase packet);
    }
}
