using Microsoft.Extensions.Logging;

namespace Sphere.Common.Helpers.Extensions
{
    public static class LoggerExtensions
    {
        public static void PacketReceived(this ILogger logger, byte[] packet, ushort clientId, LogLevel logLevel = LogLevel.Trace)
        {
            logger.Log(logLevel, "Received packet from client [{clientId}], payload [{payload}]", clientId, BitConverter.ToString(packet));
        }

        public static void PacketSent(this ILogger logger, byte[] packet, ushort clientId, LogLevel logLevel = LogLevel.Trace)
        {
            logger.Log(logLevel, "Sent packet to client [{clientId}], payload [{payload}]", clientId, BitConverter.ToString(packet));
        }
    }
}
