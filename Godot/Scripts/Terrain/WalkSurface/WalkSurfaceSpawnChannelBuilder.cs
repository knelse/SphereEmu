using System.Collections.Generic;
using SphServer.Godot.Scripts.Terrain;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Deprecated alias — use <see cref="WalkSurfaceWalkableBuilder" />.
/// </summary>
public static class WalkSurfaceSpawnChannelBuilder
{
    public static void FinalizeAll(IEnumerable<WalkSurfaceChunkBuilder> builders)
    {
        WalkSurfaceWalkableBuilder.FinalizeAll(builders);
    }

    public static void Finalize(WalkSurfaceChunkBuilder builder)
    {
        WalkSurfaceWalkableBuilder.Finalize(builder);
    }
}
