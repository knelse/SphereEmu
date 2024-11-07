using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Readers;
using Sphere.Common.Interfaces.Tcp;

namespace Sphere.Services.Readers
{
    /// <summary>
    /// Reader that reads provided stream as following:
    /// 1) Read first 2 bytes which should contain the next packet length. If nothing red - return false
    /// 2) Parse packet length and read related amount of bytes from the stream
    /// 3) Create base packet which is representation of a packet received from client
    /// </summary>
    public class SpherePacketReader : IPacketReader
    {
        private readonly IClientAccessor _tcpClientAccessor;
        private PacketBase  _current;

        public SpherePacketReader(IClientAccessor tcpClientAccessor)
        {
            _tcpClientAccessor = tcpClientAccessor;
        }

        public PacketBase Current => _current;

        public ValueTask DisposeAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Move forward trough a stream by one packet at a time
        /// </summary>
        /// <returns></returns>
        public async ValueTask<bool> MoveNextAsync()
        {
            var buff = new byte[4096];

            var count = await _tcpClientAccessor.Client.ReadAsync(buff, 0, 2);

            if (count == 0)
            {
                return false;
            }

            var packetSize = BitConverter.ToUInt16(buff, 0);

            var read = await _tcpClientAccessor.Client.ReadAsync(buff, 2, packetSize - 2);

            if (read != packetSize - 2)
            {
                return false;
            }

            var basePacket = new PacketBase(buff[..packetSize], _tcpClientAccessor.ClientId);

            _current = basePacket;

            return true;
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }
    }
}
