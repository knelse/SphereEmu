using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Common.Packets.Client
{
    public class CharacterSelectPacket : Packet
    {
        public CharacterSelectPacket(PacketBase packet) : base(packet)
        {
        }

        public int SelectedIndex => OriginalMessage[17] / 4 - 1;

        public override Task Handle(IPacketHandler handler, CancellationToken cancellationToken)
        {
            return handler.Handle(this, cancellationToken);
        }
    }
}
