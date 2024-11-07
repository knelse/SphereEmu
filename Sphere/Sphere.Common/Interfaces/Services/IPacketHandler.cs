using Sphere.Common.Interfaces.Packets;

namespace Sphere.Common.Interfaces.Services
{

    public interface IPacketHandler
    {
        Task Handle<T>(T packet, CancellationToken cancellationToken) where T : IPacket;
    }

    public interface IPacketHandler<T> where T : IPacket
    {
        Task Handle(IPacket packet, CancellationToken cancellationToken);
    }
}
