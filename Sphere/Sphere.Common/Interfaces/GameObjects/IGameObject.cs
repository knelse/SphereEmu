namespace Sphere.Common.Interfaces.GameObjects
{
    /// <summary>
    /// Basic interface for any in-game object.
    /// </summary>
    public interface IGameObject
    {
        ushort EntityId { get; set; }
    }
}
