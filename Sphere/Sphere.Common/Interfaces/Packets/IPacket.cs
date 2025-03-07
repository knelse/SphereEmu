using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Common.Interfaces.Packets
{
    public interface IPacket
    {
        byte[] OriginalMessage { get; }

        PacketType PacketType { get; }

        int Size { get; }

        Task Handle(IPacketHandler handler, CancellationToken cancellationToken);
    }
}
