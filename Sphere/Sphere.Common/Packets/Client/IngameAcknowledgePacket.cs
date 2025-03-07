using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Common.Packets.Client
{
    public class IngameAcknowledgePacket : Packet, IPacket
    {
        public readonly PacketBase BasePacket;

        public IngameAcknowledgePacket(PacketBase basePacket) : base(basePacket)
        {
            BasePacket = basePacket ?? throw new ArgumentNullException(nameof(basePacket));
        }

        public override PacketType PacketType => PacketType.IngameAcknowledge;

        public override Task Handle(IPacketHandler handler, CancellationToken cancellationToken)
        {
            return handler.Handle(this, cancellationToken);
        }
    }
}
