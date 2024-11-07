using Microsoft.Extensions.Logging;
using Sphere.Common.Helpers;
using Sphere.Common.Helpers.Extensions;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets;

namespace Sphere.Services.Services.Handlers
{
    public class IngamePingPacketHandler : BaseHandler, IPacketHandler<IngamePingPacket>
    {
        private readonly object _lock = new object();

        public IngamePingPacketHandler(ILogger<IngamePingPacketHandler> logger, IClientAccessor tcpClientAccessor) : base(logger, tcpClientAccessor)
        {
        }

        public async Task Handle(IPacket packet, CancellationToken cancellationToken)
        {
            if (packet is not IngamePingPacket ingamePingPacket)
            {
                throw new ArgumentException($"Packet is not type of [{typeof(IngamePingPacket)}]");
            }

            var clientPingBytesForPong = ingamePingPacket.OriginalMessage[9..30];
            
            var coords = CoordinatesHelper.GetCoordsFromPingBytes(ingamePingPacket.OriginalMessage);

            _clientAccessor.Character.Coordinates = coords;

            // [TODO]: store character coords

            var xored = clientPingBytesForPong[5];
            
            var pong = new byte[]
            {
                0x12,
                0x00,
                0x2C,
                0x01,
                0x00,
                clientPingBytesForPong[0],
                clientPingBytesForPong[1],
                clientPingBytesForPong[2],
                clientPingBytesForPong[3],
                clientPingBytesForPong[4],
                xored,
                BitHelper.MinorByte(_clientAccessor.PingCounter),
                BitHelper.MajorByte(_clientAccessor.PingCounter),
                clientPingBytesForPong[8],
                clientPingBytesForPong[9],
                clientPingBytesForPong[10],
                clientPingBytesForPong[11],
                0x00
            };

            await SendPacket(pong);

            await SendPacket(CommonPackets.TransmissionEndPacket);

            _clientAccessor.PingCounter++;
            _clientAccessor.LastPing = ingamePingPacket.OriginalMessage;
        }
    }
}
