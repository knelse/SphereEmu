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
    public TerrainMeshHeightSnapshot? TerrainHeights { get; init; }

    public static SpawnSlotBakeContext Create(
        Vector3 origin,
        float spawnRadiusMeters,
        TerrainMeshHeightSnapshot? terrainHeights = null)
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
            TerrainHeights = terrainHeights,
        };
    }

    public static SpawnSlotBakeContext Create(MonsterSpawner spawner, Vector3 origin)
        => Create(origin, spawner.SpawnRadiusMeters);
}
