using System;
using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Fast outdoor spawn queries backed by the pre-baked walkable field.
/// </summary>
public static class WalkSurfaceWalkableQuery
{
    public static bool TryFindValidWalkSurface(
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition)
    {
        spawnWorldPosition = default;
        if (!WalkSurfaceCache.HasWalkableField)
        {
            return false;
        }

        if (!WalkSurfaceCache.TrySampleWalkableGround(worldProbeOrigin.X, worldProbeOrigin.Z, out var worldY))
        {
            return false;
        }

        var groundPoint = new Vector3(worldProbeOrigin.X, worldY, worldProbeOrigin.Z);
        if (!WalkSurfaceCache.IsSpawnFootprintAcceptable(groundPoint.X, groundPoint.Z))
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

    public static bool TryPickSpawnSlots(
        Vector3 spawnerOrigin,
        float spawnRadiusMeters,
        int targetCount,
        float minSeparationMeters,
        IReadOnlyList<Vector3>? existingOccupied,
        out List<(float X, float Z, float Y)> slots)
    {
        slots = [];
        if (targetCount <= 0 || !WalkSurfaceCache.HasWalkableField)
        {
            return false;
        }

        WalkSurfaceCache.PreloadChunksForRadius(
            spawnerOrigin.X,
            spawnerOrigin.Z,
            spawnRadiusMeters + WalkSurfaceCache.MobSpawnBodyRadiusMeters + 1f);
        var candidates = new List<(float X, float Z, float Y)>();
        WalkSurfaceCache.CollectWalkableCandidates(spawnerOrigin.X, spawnerOrigin.Z, spawnRadiusMeters, candidates);
        if (candidates.Count == 0)
        {
            return false;
        }

        ShuffleCandidates(candidates);

        var occupied = new List<Vector3>();
        if (existingOccupied is not null)
        {
            foreach (var position in existingOccupied)
            {
                occupied.Add(position);
            }
        }

        foreach (var (x, z, y) in candidates)
        {
            var candidate = new Vector3(x, y, z);
            if (!WalkSurfaceCache.IsSpawnFootprintAcceptable(x, z))
            {
                continue;
            }

            if (HasOverlap(candidate, minSeparationMeters, occupied))
            {
                continue;
            }

            slots.Add((x, z, y));
            occupied.Add(candidate);
            if (slots.Count >= targetCount)
            {
                return true;
            }
        }

        return slots.Count > 0;
    }

    private static void ShuffleCandidates(List<(float X, float Z, float Y)> candidates)
    {
        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
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
