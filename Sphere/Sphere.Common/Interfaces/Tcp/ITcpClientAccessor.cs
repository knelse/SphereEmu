using Sphere.Common.Enums;

namespace Sphere.Common.Interfaces.Tcp
{
    public interface ITcpClientAccessor
    {
        ITcpClient Client { get; set; }

        ushort ClientId { get; set; }

        ClientState ClientState { get; set; }

        Guid PlayerId { get; set; }
    }
}
