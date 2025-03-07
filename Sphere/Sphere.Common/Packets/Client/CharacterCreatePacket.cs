using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Common.Packets.Client
{
    public class CharacterCreatePacket : Packet, IPacket
    {
        public CharacterCreatePacket(PacketBase packet) : base(packet)
        {
        }

        public byte[] SyncBytes1 => _packet.OriginalMessage[2..6]; // 0x7CBDD801

        public byte[] ConfirmationBytes => _packet.OriginalMessage[6..9]; // 0x2C0100

        public byte[] SyncBytes2 => _packet.OriginalMessage[9..13]; // 0x00052FC7

        public byte[] BeginPayload => _packet.OriginalMessage[13..15]; // 0x08 0x40

        public override PacketType PacketType => PacketType.CreateCharacter;

        public byte LoginLength => _packet.OriginalMessage[16]; // 0x45

        public byte CharacterIndex => _packet.OriginalMessage[17];

        public byte[] Nickname => _packet.OriginalMessage[20..^5];

        public byte[] CharacterInfo => _packet.OriginalMessage[^5..];


        public override Task Handle(IPacketHandler handler, CancellationToken cancellationToken)
        {
            return handler.Handle(this, cancellationToken);
        }
    }
}
