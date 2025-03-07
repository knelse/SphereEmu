using Sphere.Common.Entities;
using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Nodes;

namespace Sphere.Common.Interfaces.Tcp
{
    public interface IClientAccessor
    {
        IServer Server { get; init; }

        ITcpClient Client { get; set; }

        IClient GameClient { get; set; }

        ushort ClientId { get; set; }

        ClientState ClientState { get; set; }

        Guid PlayerId { get; set; }

        CharacterEntity Character { get; set; }

        byte[] LastPing { get; set; }

        ushort PingCounter { get; set; }
    }
}
