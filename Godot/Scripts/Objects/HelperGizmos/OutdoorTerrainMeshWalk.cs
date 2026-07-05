using System;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Outdoor walk candidacy from live GridMap terrain when the walk atlas is stale or over-blocked.
/// </summary>
public static class OutdoorTerrainMeshWalk
{
    /// <summary>
    ///     When atlas blocked cells have a ground height matching mesh height, keep the block.
    /// </summary>
    public const float MaxBlockedAtlasTerrainDeltaMeters = 1f;

    /// <summary>
    ///     Reject atlas walkable samples far above outdoor terrain mesh (building roofs in object JSON).
    /// </summary>
    public const float MaxWalkableAboveTerrainMeshMeters = 1f;

    public static bool IsOutdoorWalkCandidate(
        float worldX,
        float worldZ,
        TerrainMeshHeightSnapshot? terrainHeights)
    {
        if (terrainHeights is null || !terrainHeights.TrySample(worldX, worldZ, out var meshY))
        {
            return false;
        }

        if (!WalkSurfaceCache.HasAnyChunkFiles())
        {
            return true;
        }

        if (WalkSurfaceCache.TrySampleWalkableGround(worldX, worldZ, out var walkableY)
            && !float.IsNaN(walkableY)
            && walkableY - meshY > MaxWalkableAboveTerrainMeshMeters)
        {
            return false;
        }

        if (WalkSurfaceCache.IsBlocked(worldX, worldZ)
            && WalkSurfaceCache.TrySampleGround(worldX, worldZ, out var atlasY)
            && !float.IsNaN(atlasY)
            && Math.Abs(atlasY - meshY) <= MaxBlockedAtlasTerrainDeltaMeters)
        {
            return false;
        }

        return true;
    }
}
