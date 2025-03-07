using Microsoft.Extensions.Logging;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets.Client;

namespace Sphere.Services.Services.Handlers
{
    public class IngameAcknowledgePacketHandler : BaseHandler, IPacketHandler<IngameAcknowledgePacket>
    {
        public IngameAcknowledgePacketHandler(ILogger<IngameAcknowledgePacketHandler> logger, IClientAccessor tcpClientAccessor) : base(logger, tcpClientAccessor)
        {
        }

        public async Task Handle(IPacket packet, CancellationToken cancellationToken)
        {
            if (packet is not IngameAcknowledgePacket acknowledgePacket)
            {
                throw new ArgumentException($"Packet is not type of [{typeof(IngameAcknowledgePacket)}]");
            }

            _clientAccessor.ClientState = Common.Enums.ClientState.INGAME_DEFAULT;
            _clientAccessor.GameClient.ClientConnected();
        }
    }
}
