using Godot;

namespace Sphere.Common.Interfaces.Nodes
{
    public interface IClient
    {
        bool IsInGame { get; }

        Node Node { get; }

        void ClientConnected();
    }
}
