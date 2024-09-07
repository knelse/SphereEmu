using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Tcp;

namespace Sphere.Services.Misc
{
    public class TcpClientAccessor : ITcpClientAccessor
    {
        public ITcpClient Client { get; set; }

        public ushort ClientId { get; set; }

        public ClientState ClientState { get; set; }

        public Guid PlayerId { get; set; }
    }
}

