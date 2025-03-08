using Sphere.Common.Interfaces.GameObjects;

namespace Sphere.Common.Events.SpawnObject
{
    /// <summary>
    /// Represents an event args for any spawnable object in a game.
    /// </summary>
    public class SpawnObjectEventArgs : EventArgs
    {
        public ISpawnable Object { get; set; }
    }
}
