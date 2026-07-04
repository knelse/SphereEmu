using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Pre-bakes validated outdoor spawn slots from the unified walkable field.
/// </summary>
public static class MonsterSpawnSlotBaker
{
    private const string ErrorNamePrefix = "ERROR - ";

    public static int BakeForSpawner(MonsterSpawner spawner)
    {
        var targetCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
        if (targetCount <= 0)
        {
            spawner.ClearSpawnError();
            spawner.SetBakedSpawnSlots([]);
            return 0;
        }

        if (!WalkSurfaceCache.HasAnyChunkFiles())
        {
            MarkBakeFailure(spawner, targetCount, 0, "no walk atlas");
            return 0;
        }

        if (!WalkSurfaceCache.HasWalkableField)
        {
            MarkBakeFailure(
                spawner,
                targetCount,
                0,
                "walk chunks have no walkable field — rebake walk surface with --objects-only or full bake first");
            return 0;
        }

        if (OutdoorNavCache.HasAnyNavFiles())
        {
            OutdoorNavCache.PreloadForRadius(
                spawner.LeashCenterWorld.X,
                spawner.LeashCenterWorld.Z,
                spawner.LeashRadiusMeters);
        }

        WalkSurfaceCache.PreloadChunksForRadius(
            spawner.GlobalPosition.X,
            spawner.GlobalPosition.Z,
            spawner.SpawnRadiusMeters + 1f);

        var origin = spawner.GlobalPosition;
        if (!WalkSurfaceWalkableQuery.TryPickSpawnSlots(
                origin,
                spawner.SpawnRadiusMeters,
                targetCount * 4,
                OutdoorFieldConfig.MinSlotSeparationMeters,
                existingOccupied: null,
                out var rawCandidates))
        {
            rawCandidates = [];
        }

        Shuffle(rawCandidates);
        var validated = new List<Vector3>(targetCount);
        var picked = new List<Vector3>();
        OutdoorSpawnSlotValidator.FailReason? lastFailure = null;
        foreach (var (x, z, y) in rawCandidates)
        {
            if (validated.Count >= targetCount)
            {
                break;
            }

            var candidate = new Vector3(x, y, z);
            if (!IsSeparated(candidate, picked, OutdoorFieldConfig.MinSlotSeparationMeters))
            {
                continue;
            }

            if (!OutdoorSpawnSlotValidator.TryValidateCandidate(spawner, candidate, origin, out var reason))
            {
                lastFailure = reason;
                continue;
            }

            validated.Add(candidate);
            picked.Add(candidate);
        }

        if (validated.Count < targetCount)
        {
            var detail = lastFailure?.ToString() ?? "insufficient walkable candidates";
            MarkBakeFailure(spawner, targetCount, validated.Count, detail);
        }
        else
        {
            spawner.ClearSpawnError();
        }

        spawner.SetBakedSpawnSlots(validated);
        return validated.Count;
    }

    public static int BakeAllUnder(Node parent)
    {
        var baked = 0;
        var slotCount = 0;
        var errors = 0;
        foreach (var child in parent.GetChildren())
        {
            if (child is not MonsterSpawner spawner || !GodotObject.IsInstanceValid(spawner))
            {
                continue;
            }

            slotCount += BakeForSpawner(spawner);
            if (spawner.HasSpawnError)
            {
                errors++;
            }

            baked++;
        }

        GD.Print($"MonsterSpawnSlotBaker: baked {slotCount} slot(s) across {baked} spawner(s), {errors} ERROR.");
        return slotCount;
    }

    private static void MarkBakeFailure(MonsterSpawner spawner, int targetCount, int foundCount, string detail)
    {
        GD.PushWarning(
            $"MonsterSpawnSlotBaker: spawner '{spawner.Name}' at {spawner.GlobalPosition}: "
            + $"found {foundCount}/{targetCount} slot(s) ({detail}).");
        spawner.MarkSpawnError();
        spawner.SetBakedSpawnSlots([]);
    }

    private static bool IsSeparated(Vector3 candidate, IReadOnlyList<Vector3> picked, float minSeparationMeters)
    {
        var minSeparationSq = minSeparationMeters * minSeparationMeters;
        foreach (var position in picked)
        {
            var dx = candidate.X - position.X;
            var dz = candidate.Z - position.Z;
            if (dx * dx + dz * dz < minSeparationSq)
            {
                return false;
            }
        }

        return true;
    }

    private static void Shuffle(List<(float X, float Z, float Y)> candidates)
    {
        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
    }
}
