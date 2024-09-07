using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Common.Packets
{
    public class ClientPingPacketLong : Packet, IPacket
    {
        public readonly PacketBase BasePacket;

        public ClientPingPacketLong(PacketBase basePacket) : base(basePacket)
        {
            BasePacket = basePacket ?? throw new ArgumentNullException(nameof(basePacket));
        }

        public override PacketType PacketType => PacketType.ClientPingLong;

        public override Task Handle(IPacketHandler handler, CancellationToken cancellationToken)
        {
            return handler.Handle(this, cancellationToken);
        }
    }
}
