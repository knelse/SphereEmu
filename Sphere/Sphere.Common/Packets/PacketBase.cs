using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Common.Interfaces.Packets
{
    public class PacketBase : IPacket
    {
        protected readonly byte[] _originalMessage;
        protected readonly ushort _clientId; // probably should read from package itself

        public int Size => BitConverter.ToUInt16(_originalMessage, 0);

        public byte[] PayloadRaw => _originalMessage[2..];

        public virtual PacketType PacketType => (PacketType)_originalMessage[15];

        public byte[] OriginalMessage => _originalMessage;

        public PacketBase(byte[] message, ushort clientId)
        {
            _originalMessage = message;
            _clientId = clientId;
        }

        public override string ToString()
        {
            return BitConverter.ToString(_originalMessage, 0, Size);
        }

        public Task Handle(IPacketHandler handler, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
