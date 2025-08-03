using System.Threading.Tasks;

namespace SphServer.Client.Networking.Handlers;

public interface ISphereClientNetworkingHandler
{
    public Task Handle (double delta);
}