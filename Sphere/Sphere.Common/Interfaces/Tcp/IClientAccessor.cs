using Godot;
using Sphere.Common.Entities;
using Sphere.Common.Enums;

namespace Sphere.Common.Interfaces.Tcp
{
    public interface IClientAccessor
    {
        ITcpClient Client { get; set; }

        ushort ClientId { get; set; }

        ClientState ClientState { get; set; }

        Guid PlayerId { get; set; }

        CharacterEntity Character { get; set; }

        byte[] LastPing { get; set; }

        ushort PingCounter { get; set; }
    }
}
