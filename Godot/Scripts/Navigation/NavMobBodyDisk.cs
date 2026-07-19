namespace SphServer.Godot.Scripts.Navigation;

/// <summary>XZ offsets for mob-body disc probes on the navmesh.</summary>
internal static class NavMobBodyDisk
{
    /// <summary>Full 8-point footprint (cardinals + diagonals).</summary>
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

    /// <summary>Cardinals only — enough for bake-time rejection, ~2× fewer nav queries.</summary>
    internal static readonly (float X, float Z)[] CardinalOffsets =
    [
        (1f, 0f),
        (-1f, 0f),
        (0f, 1f),
        (0f, -1f),
    ];
}
