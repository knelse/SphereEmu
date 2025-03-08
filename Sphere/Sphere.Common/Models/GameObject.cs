using Sphere.Common.Interfaces.GameObjects;

namespace Sphere.Common.Models
{
    /// <summary>
    /// A base game object class.
    /// </summary>
    public class GameObject : IGameObject
    {
        public ushort EntityId { get; set; }
    }
}
