using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Stamps blocked disks into walk atlas builders from terrain object JSON placements.
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
            var radius = WalkSurfaceModelBoundsCache.ResolveFootprintRadiusMeters(placement, modelsDirectory);
            var builder = WalkSurfaceAtlasBuilder.GetOrCreateBuilder(builders, origin.X, origin.Z);
            if (builder.StampBlockedDisk(origin.X, origin.Z, radius))
            {
                stamped++;
            }
        }

        return stamped;
    }
}
