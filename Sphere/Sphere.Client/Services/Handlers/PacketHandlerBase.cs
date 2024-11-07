using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Services.Services.Handlers
{
    public class PacketHandlerBase : IPacketHandler
    {
        private readonly ILogger<PacketHandlerBase> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IPacketParser _packetParser;

        public PacketHandlerBase(ILogger<PacketHandlerBase> logger, IServiceProvider serviceProvider, IPacketParser packetParser)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _packetParser = packetParser ?? throw new ArgumentNullException(nameof(packetParser));
        }

        public async Task Handle<T>(T packet, CancellationToken cancellationToken) where T : IPacket
        {
            var handler = _serviceProvider.GetRequiredService<IPacketHandler<T>>();

            await handler.Handle(packet, cancellationToken);
        }
    }
}
