using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Common.Packets
{
    public abstract class Packet : IPacket
    {
        protected readonly PacketBase _packet;

        protected Packet(PacketBase packet)
        {
            _packet = packet;
        }

        public byte[] OriginalMessage => _packet.OriginalMessage;

        public virtual PacketType PacketType => throw new NotImplementedException();

        public int Size => _packet.Size;

        public abstract Task Handle(IPacketHandler handler, CancellationToken cancellationToken);
    }
}
