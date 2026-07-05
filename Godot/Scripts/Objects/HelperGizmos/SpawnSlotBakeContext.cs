using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Per-spawner bake state to avoid repeating expensive anchor and nav work.
/// </summary>
public readonly struct SpawnSlotBakeContext
{
    public Vector3 WalkAnchor { get; init; }
    public bool HasWalkAnchor { get; init; }

    public static SpawnSlotBakeContext Create(Vector3 origin, float spawnRadiusMeters)
    {
        var hasAnchor = WalkSurfaceCache.TryFindNearestWalkAnchor(
            origin.X,
            origin.Z,
            spawnRadiusMeters,
            out var anchor);
        return new SpawnSlotBakeContext
        {
            HasWalkAnchor = hasAnchor,
            WalkAnchor = anchor,
        };
    }

    public static SpawnSlotBakeContext Create(MonsterSpawner spawner, Vector3 origin)
        => Create(origin, spawner.SpawnRadiusMeters);
}
