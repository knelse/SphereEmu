using System;
using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

public static class MonsterSpawnPlanner
{
    public static MonsterSpawnPlan Plan(
        Vector3 spawnerOrigin,
        float spawnRadiusMeters,
        int targetRegularCount,
        int targetNamedCount,
        IReadOnlyList<Vector3>? existingOccupied,
        IMonsterSpawnGroundQuery groundQuery,
        Random random)
    {
        var totalCount = targetRegularCount + targetNamedCount;
        if (totalCount > 0
            && WalkSurfaceWalkableQuery.TryPickSpawnSlots(
                spawnerOrigin,
                spawnRadiusMeters,
                totalCount,
                MonsterSpawnPlacement.MinMobSeparationMeters,
                existingOccupied,
                out var gridSlots)
            && TrySplitGridSlots(gridSlots, targetRegularCount, targetNamedCount, out var gridRegular, out var gridNamed))
        {
            return new MonsterSpawnPlan(gridRegular, gridNamed);
        }

        var placement = new MonsterSpawnPlacement(random);
        placement.Reset(existingOccupied);

        var regularPositions = new List<Vector3>(targetRegularCount);
        for (var i = 0; i < targetRegularCount; i++)
        {
            if (!placement.TryFindSpawnPosition(spawnerOrigin, spawnRadiusMeters, groundQuery, out var position))
            {
                break;
            }

            regularPositions.Add(position);
        }

        var namedPositions = new List<Vector3>(targetNamedCount);
        for (var i = 0; i < targetNamedCount; i++)
        {
            if (!placement.TryFindSpawnPosition(spawnerOrigin, spawnRadiusMeters, groundQuery, out var position))
            {
                break;
            }

            namedPositions.Add(position);
        }

        return new MonsterSpawnPlan(regularPositions, namedPositions);
    }

    private static bool TrySplitGridSlots(
        List<(float X, float Z, float Y)> gridSlots,
        int targetRegularCount,
        int targetNamedCount,
        out List<Vector3> regularPositions,
        out List<Vector3> namedPositions)
    {
        regularPositions = new List<Vector3>(targetRegularCount);
        namedPositions = new List<Vector3>(targetNamedCount);
        var index = 0;
        for (var i = 0; i < targetRegularCount && index < gridSlots.Count; i++, index++)
        {
            var slot = gridSlots[index];
            regularPositions.Add(new Vector3(slot.X, slot.Y, slot.Z));
        }

        for (var i = 0; i < targetNamedCount && index < gridSlots.Count; i++, index++)
        {
            var slot = gridSlots[index];
            namedPositions.Add(new Vector3(slot.X, slot.Y, slot.Z));
        }

        return regularPositions.Count == targetRegularCount && namedPositions.Count == targetNamedCount;
    }
}
