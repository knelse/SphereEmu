using Microsoft.Extensions.Logging;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets;
using static Sphere.Common.Packets.CommonPackets;

namespace Sphere.Services.Services.Handlers
{
    public class ClientPingPacketShortHandler : BaseHandler, IPacketHandler<ClientPingPacketShort>
    {
        public ClientPingPacketShortHandler(ILogger<ClientPingPacketShortHandler> logger, ITcpClientAccessor tcpClientAccessor) : base(logger, tcpClientAccessor)
        {
        }

        public async Task Handle(IPacket packet, CancellationToken cancellationToken)
        {
            await SendPacket(SixSecondPing(_tcpClientAccessor.ClientId));
        }
    }
}
