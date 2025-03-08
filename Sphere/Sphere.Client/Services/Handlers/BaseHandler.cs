using Microsoft.Extensions.Logging;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets;

namespace Sphere.Services.Services.Handlers
{
    public class BaseHandler
    {
        protected readonly ILogger _logger;
        protected readonly IClientAccessor _clientAccessor;

        protected BaseHandler(ILogger logger, IClientAccessor tcpClientAccessor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clientAccessor = tcpClientAccessor ?? throw new ArgumentNullException(nameof(tcpClientAccessor));
        }

        protected async Task SendPacket(byte[] rcvBuffer)
        {
            await _clientAccessor.Client.WriteAsync(rcvBuffer);
        }

        protected async Task TerminateConnection()
        {
            await SendPacket(CommonPackets.TransmissionEndPacket);
            _clientAccessor.Client.Close();

            _logger.LogWarning("Connection terminated with client {client}", _clientAccessor);
        }
    }
}
