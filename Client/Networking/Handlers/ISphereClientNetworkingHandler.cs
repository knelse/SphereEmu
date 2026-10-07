using System.Threading.Tasks;

namespace SphServer.Client.Networking.Handlers;

public interface ISphereClientNetworkingHandler
{
    /// <summary>
    /// Empty before game so those states still run their timers; in game the frame is always
    /// present
    /// </summary>
    public Task Handle (byte[] frame, double delta);
}
