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
            var origin = placement.WorldTransform.Origin;
            var builder = WalkSurfaceAtlasBuilder.GetOrCreateBuilder(builders, origin.X, origin.Z);
            if (StampPlacement(builder, placement, modelsDirectory))
            {
                stamped++;
            }
        }

        return stamped;
    }

    private static bool StampPlacement(
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

    private static bool UsesOrientedRectFootprint(TerrainObjectWalkCategory category)
    {
        return category is TerrainObjectWalkCategory.ExtraInstanced or TerrainObjectWalkCategory.Other;
    }
}
