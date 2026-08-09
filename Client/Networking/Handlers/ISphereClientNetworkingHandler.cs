using System.Threading.Tasks;

namespace SphServer.Client.Networking.Handlers;

public interface ISphereClientNetworkingHandler
{
    /// <summary>
    ///     One whole frame, or empty when the tick brought none — the before-game states still run
    ///     their timers on empty ticks. In game a handler is only invoked with a frame.
    /// </summary>
    public Task Handle (byte[] frame, double delta);
}
