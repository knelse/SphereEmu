using Godot;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Binds a monster to its spawner home slot and outdoor leash disk.
/// </summary>
public readonly struct MonsterHomeBinding
{
    public MonsterHomeBinding(int slotIndex, Vector3 homeSlotWorld)
    {
        SlotIndex = slotIndex;
        HomeSlotWorld = homeSlotWorld;
    }

    public int SlotIndex { get; }
    public Vector3 HomeSlotWorld { get; }
}

public enum MonsterLeashPhase
{
    Inside,
    AtBoundary,
}
