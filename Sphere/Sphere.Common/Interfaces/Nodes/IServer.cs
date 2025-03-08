using Sphere.Common.Events.SpawnObject;
using Sphere.Common.Models;

namespace Sphere.Common.Interfaces
{
    /// <summary>
    /// An interface of server component of the game.
    /// </summary>
    public interface IServer
    {
        /// <summary>
        /// Starts server listener.
        /// </summary>
        /// <returns></returns>
        Task StartAsync();

        /// <summary>
        /// Stops server listener.
        /// </summary>
        /// <returns></returns>
        Task StopAsync();

        /// <summary>
        /// Spawn mob command. Most likely will be generalized in future.
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="model"></param>
        void SpawnMob(int clientId, SpawnMobModel model);

        /// <summary>
        /// An event raised upon spawning an object so to notify clients about new object.
        /// </summary>
        event EventHandler<SpawnObjectEventArgs> SpawnEvent;
    }
}
