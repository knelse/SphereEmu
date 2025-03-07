using Sphere.Common.Entities;
using Sphere.Common.Enums;
using Sphere.Common.Interfaces;
using Sphere.Common.Interfaces.Nodes;
using Sphere.Common.Interfaces.Tcp;

namespace Sphere.Services.Misc
{
    public class ClientAccessor : IClientAccessor
    {
        public ITcpClient Client { get; set; }

        public ushort ClientId { get; set; }

        public ClientState ClientState { get; set; }

        public Guid PlayerId { get; set; }

        public CharacterEntity? Character { get; set; }

        public byte[] LastPing { get; set; }

        public ushort PingCounter { get; set; }

        public IServer Server { get; init; }

        public IClient GameClient { get; set; }

        public override string ToString()
        {
            return $"ClientId: {ClientId}, PlayerId: {PlayerId}, ClientState: {ClientState}";
        }
    }
}

