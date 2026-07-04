using System.Collections.Generic;
using SphServer.Godot.Scripts.Terrain;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Finalizes the unified outdoor walkable mask after terrain + object footprints are stamped.
/// </summary>
public static class WalkSurfaceWalkableBuilder
{
    public static void FinalizeAll(IEnumerable<WalkSurfaceChunkBuilder> builders)
    {
        foreach (var builder in builders)
        {
            Finalize(builder);
        }
    }

    public static void Finalize(WalkSurfaceChunkBuilder builder)
    {
        builder.EnsureTerrainBaseline();
        builder.DilateBlocked(OutdoorFieldConfig.BlockedDilationRadiusCells);
        builder.FinalizeWalkable();
    }
}
