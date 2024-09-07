using Microsoft.Extensions.Logging;
using Sphere.Common.Helpers.Extensions;
using Sphere.Common.Interfaces.Tcp;

namespace Sphere.Services.Services.Handlers
{
    public class BaseHandler
    {
        protected readonly ILogger _logger;
        protected readonly ITcpClientAccessor _tcpClientAccessor;

        protected BaseHandler(ILogger logger, ITcpClientAccessor tcpClientAccessor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tcpClientAccessor = tcpClientAccessor ?? throw new ArgumentNullException(nameof(tcpClientAccessor));
        }

        protected async Task SendPacket(byte[] rcvBuffer)
        {
            await _tcpClientAccessor.Client.WriteAsync(rcvBuffer);

            _logger.PacketSent(rcvBuffer, _tcpClientAccessor.ClientId);
        }

    }
}
