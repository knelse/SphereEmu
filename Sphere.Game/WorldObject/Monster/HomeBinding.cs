using Godot;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
/// Spawner home slot and outdoor leash disk
/// </summary>
public readonly struct MonsterHomeBinding
{
    public MonsterHomeBinding (
        int slotIndex,
        Vector3 homeSlotWorld,
        Vector3 leashCenterWorld,
        float leashRadiusMeters,
        NodePath ownerSpawnerPath,
        ulong ownerSpawnerInstanceId,
        float atlasVerticalDelta)
    {
        SlotIndex = slotIndex;
        HomeSlotWorld = homeSlotWorld;
        LeashCenterWorld = leashCenterWorld;
        LeashRadiusMeters = leashRadiusMeters;
        OwnerSpawnerPath = ownerSpawnerPath;
        OwnerSpawnerInstanceId = ownerSpawnerInstanceId;
        AtlasVerticalDelta = atlasVerticalDelta;
    }

    public int SlotIndex { get; }
    public Vector3 HomeSlotWorld { get; }
    public Vector3 LeashCenterWorld { get; }
    public float LeashRadiusMeters { get; }
    public NodePath OwnerSpawnerPath { get; }
    public ulong OwnerSpawnerInstanceId { get; }
    /// <summary>
    /// Always 0; pathing uses navmesh Y and the constructor still takes this slot
    /// </summary>
    public float AtlasVerticalDelta { get; }
}
