using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Builds the outdoor-only spawn channel after terrain + object walk data are stamped.
/// </summary>
public static class WalkSurfaceSpawnChannelBuilder
{
    public const float MaxOutdoorSpawnHeightDeltaMeters = 0.35f;
    public const int BlockedDilationRadiusCells = 1;

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
        builder.DilateBlocked(BlockedDilationRadiusCells);
        builder.FinalizeOutdoorSpawnAllowed(MaxOutdoorSpawnHeightDeltaMeters);
    }
}
