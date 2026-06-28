using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Stamps blocked regions into walk atlas builders from terrain object JSON placements.
/// </summary>
public static class WalkSurfaceObjectFootprintStamper
{
    public static int StampPlacements(
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders,
        IReadOnlyList<TerrainObjectPlacement> placements,
        string modelsDirectory)
    {
        var stamped = 0;
        foreach (var placement in placements)
        {
            if (StampPlacementAcrossChunks(builders, placement, modelsDirectory))
            {
                stamped++;
            }
        }

        return stamped;
    }

    private static bool StampPlacementAcrossChunks(
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders,
        TerrainObjectPlacement placement,
        string modelsDirectory)
    {
        if (!TryGetFootprintWorldBounds(placement, modelsDirectory, out var minWorldX, out var maxWorldX, out var minWorldZ, out var maxWorldZ))
        {
            return false;
        }

        var chunkMinX = FloorDiv(minWorldX, WalkSurfaceAtlasBuilder.ChunkSizeMeters);
        var chunkMaxX = FloorDiv(maxWorldX, WalkSurfaceAtlasBuilder.ChunkSizeMeters);
        var chunkMinZ = FloorDiv(minWorldZ, WalkSurfaceAtlasBuilder.ChunkSizeMeters);
        var chunkMaxZ = FloorDiv(maxWorldZ, WalkSurfaceAtlasBuilder.ChunkSizeMeters);
        var stamped = false;

        for (var chunkZ = chunkMinZ; chunkZ <= chunkMaxZ; chunkZ++)
        {
            for (var chunkX = chunkMinX; chunkX <= chunkMaxX; chunkX++)
            {
                var probeX = chunkX * WalkSurfaceAtlasBuilder.ChunkSizeMeters + 0.1f;
                var probeZ = chunkZ * WalkSurfaceAtlasBuilder.ChunkSizeMeters + 0.1f;
                var builder = WalkSurfaceAtlasBuilder.GetOrCreateBuilder(builders, probeX, probeZ);
                if (StampPlacementOnBuilder(builder, placement, modelsDirectory))
                {
                    stamped = true;
                }
            }
        }

        return stamped;
    }

    private static bool StampPlacementOnBuilder(
        WalkSurfaceChunkBuilder builder,
        TerrainObjectPlacement placement,
        string modelsDirectory)
    {
        var origin = placement.WorldTransform.Origin;
        if (UsesOrientedRectFootprint(placement.Category))
        {
            if (!WalkSurfaceModelBoundsCache.TryResolveFootprintHalfExtents(placement, modelsDirectory, out var halfExtentX, out var halfExtentZ))
            {
                return false;
            }

            var yaw = placement.WorldTransform.Basis.GetEuler(EulerOrder.Yxz).Y;
            return builder.StampBlockedOrientedRect(origin.X, origin.Z, halfExtentX, halfExtentZ, yaw);
        }

        var radius = WalkSurfaceModelBoundsCache.ResolveFootprintRadiusMeters(placement, modelsDirectory);
        return builder.StampBlockedDisk(origin.X, origin.Z, radius);
    }

    private static bool TryGetFootprintWorldBounds(
        TerrainObjectPlacement placement,
        string modelsDirectory,
        out float minWorldX,
        out float maxWorldX,
        out float minWorldZ,
        out float maxWorldZ)
    {
        minWorldX = maxWorldX = minWorldZ = maxWorldZ = 0f;
        var origin = placement.WorldTransform.Origin;

        if (UsesOrientedRectFootprint(placement.Category))
        {
            if (!WalkSurfaceModelBoundsCache.TryResolveFootprintHalfExtents(placement, modelsDirectory, out var halfExtentX, out var halfExtentZ))
            {
                return false;
            }

            var yaw = placement.WorldTransform.Basis.GetEuler(EulerOrder.Yxz).Y;
            var cos = Mathf.Cos(yaw);
            var sin = Mathf.Sin(yaw);
            var boundHalfX = Mathf.Abs(cos * halfExtentX) + Mathf.Abs(sin * halfExtentZ);
            var boundHalfZ = Mathf.Abs(sin * halfExtentX) + Mathf.Abs(cos * halfExtentZ);
            minWorldX = origin.X - boundHalfX;
            maxWorldX = origin.X + boundHalfX;
            minWorldZ = origin.Z - boundHalfZ;
            maxWorldZ = origin.Z + boundHalfZ;
            return true;
        }

        var radius = WalkSurfaceModelBoundsCache.ResolveFootprintRadiusMeters(placement, modelsDirectory);
        minWorldX = origin.X - radius;
        maxWorldX = origin.X + radius;
        minWorldZ = origin.Z - radius;
        maxWorldZ = origin.Z + radius;
        return true;
    }

    private static bool UsesOrientedRectFootprint(TerrainObjectWalkCategory category)
    {
        return category is TerrainObjectWalkCategory.ExtraInstanced or TerrainObjectWalkCategory.Other;
    }

    private static int FloorDiv(float value, float divisor)
    {
        return (int)Mathf.Floor(value / divisor);
    }
}
