using System;
using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Deprecated alias — use <see cref="WalkSurfaceWalkableQuery" />.
/// </summary>
public static class WalkSurfaceOutdoorSpawnQuery
{
    public static bool TryFindValidOutdoorSpawnSurface(
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition)
    {
        return WalkSurfaceWalkableQuery.TryFindValidWalkSurface(
            worldProbeOrigin,
            minSeparationMeters,
            occupiedWorldPositions,
            out spawnWorldPosition);
    }

    public static bool TryPickSpawnSlots(
        Vector3 spawnerOrigin,
        float spawnRadiusMeters,
        int targetCount,
        float minSeparationMeters,
        IReadOnlyList<Vector3>? existingOccupied,
        out List<Vector3> slots)
    {
        slots = [];
        if (!WalkSurfaceWalkableQuery.TryPickSpawnSlots(
                spawnerOrigin,
                spawnRadiusMeters,
                targetCount,
                minSeparationMeters,
                existingOccupied,
                out var rawSlots))
        {
            return false;
        }

        foreach (var (x, z, y) in rawSlots)
        {
            slots.Add(new Vector3(x, y, z));
        }

        return slots.Count > 0;
    }
}
