using Sphere.Common.Interfaces.Packets;

namespace Sphere.Common.Interfaces.Readers
{
    public interface IPacketReader : IAsyncEnumerator<PacketBase>
    {
    }
}
