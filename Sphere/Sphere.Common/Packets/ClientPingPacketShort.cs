using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Common.Packets
{
    public class ClientPingPacketShort : Packet, IPacket
    {
        public readonly PacketBase BasePacket;

        public ClientPingPacketShort(PacketBase basePacket) : base(basePacket)
        {
            BasePacket = basePacket ?? throw new ArgumentNullException(nameof(basePacket));
        }

        public override PacketType PacketType => PacketType.ClientPingShort;

        public override Task Handle(IPacketHandler handler, CancellationToken cancellationToken)
        {
            return handler.Handle(this, cancellationToken);
        }
    }
}
