using Godot;

namespace Sphere.Common.Interfaces.Nodes
{
    public interface IClient
    {
        Task InitializePlayer();

        bool IsInGame { get; }

        Node Node { get; }
    }
}
