using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Common.Packets
{
    public class IngamePingPacket : Packet, IPacket
    {
        public readonly PacketBase BasePacket;

        public IngamePingPacket(PacketBase basePacket) : base(basePacket)
        {
            BasePacket = basePacket ?? throw new ArgumentNullException(nameof(basePacket));
        }

        public override PacketType PacketType => PacketType.IngamePingPacket;

        public override Task Handle(IPacketHandler handler, CancellationToken cancellationToken)
        {
            return handler.Handle(this, cancellationToken);
        }
    }
}
