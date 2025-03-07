using Microsoft.Extensions.Logging;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets.Client;
using static Sphere.Common.Packets.CommonPackets;

namespace Sphere.Services.Services.Handlers
{
    public class ClientPingPacketLongHandler : BaseHandler, IPacketHandler<ClientPingPacketLong>
    {
        public ClientPingPacketLongHandler(ILogger<ClientPingPacketLongHandler> logger, IClientAccessor tcpClientAccessor) : base(logger, tcpClientAccessor)
        {
        }

        public async Task Handle(IPacket packet, CancellationToken cancellationToken)
        {
            await SendPacket(FifteenSecondPing(_clientAccessor.ClientId));
        }
    }
}
