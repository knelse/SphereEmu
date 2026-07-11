using Godot;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Binds a monster to its spawner home slot and outdoor leash disk.
/// </summary>
public readonly struct MonsterHomeBinding
{
    public MonsterHomeBinding(
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
    ///     walk-surface atlas Y minus Godot terrain Y at bind time; subtract from atlas samples at runtime.
    /// </summary>
    public float AtlasVerticalDelta { get; }
}
