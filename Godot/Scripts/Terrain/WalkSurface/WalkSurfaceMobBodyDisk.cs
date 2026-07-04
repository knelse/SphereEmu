namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

internal static class WalkSurfaceMobBodyDisk
{
    internal static readonly (float X, float Z)[] Offsets =
    [
        (1f, 0f),
        (-1f, 0f),
        (0f, 1f),
        (0f, -1f),
        (0.70710677f, 0.70710677f),
        (-0.70710677f, 0.70710677f),
        (0.70710677f, -0.70710677f),
        (-0.70710677f, -0.70710677f),
    ];
}
