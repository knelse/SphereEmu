using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Outdoor spawn/placement height queries backed by the prebuilt walk atlas.
///     Indoor regions are not covered yet — callers should fall back when sampling fails.
/// </summary>
public static class WalkSurfaceQuery
{
    public static bool TrySampleGround(Vector3 worldProbeOrigin, out Vector3 groundPoint)
    {
        groundPoint = default;
        if (!WalkSurfaceCache.TrySampleGround(worldProbeOrigin.X, worldProbeOrigin.Z, out var worldY))
        {
            return false;
        }

        groundPoint = new Vector3(worldProbeOrigin.X, worldY, worldProbeOrigin.Z);
        return true;
    }

    public static bool TryFindValidSpawnSurface(
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition)
    {
        spawnWorldPosition = default;

        if (!TrySampleGround(worldProbeOrigin, out var groundPoint))
        {
            return false;
        }

        if (HasOverlap(groundPoint, minSeparationMeters, occupiedWorldPositions))
        {
            return false;
        }

        spawnWorldPosition = groundPoint;
        return true;
    }

    private static bool HasOverlap(
        Vector3 candidate,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions)
    {
        var minSeparationSq = minSeparationMeters * minSeparationMeters;
        foreach (var occupied in occupiedWorldPositions)
        {
            var dx = candidate.X - occupied.X;
            var dz = candidate.Z - occupied.Z;
            if (dx * dx + dz * dz < minSeparationSq)
            {
                return true;
            }
        }

        return false;
    }
}
