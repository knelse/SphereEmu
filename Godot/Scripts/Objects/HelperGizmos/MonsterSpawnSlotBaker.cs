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
        var mobCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
        if (mobCount <= 0)
        {
            spawner.ClearSpawnError();
            spawner.SetBakedSpawnSlots([]);
            return 0;
        }

        var poolCount = Math.Max(mobCount, OutdoorFieldConfig.MinBakedSpawnSlotsPerSpawner);

        if (!WalkSurfaceCache.HasAnyChunkFiles())
        {
            MarkBakeFailure(spawner, mobCount, poolCount, 0, "no walk atlas");
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
            Mathf.Max(spawner.SpawnRadiusMeters, spawner.LeashRadiusMeters) + 1f);

        if (!WalkSurfaceCache.HasWalkableField)
        {
            MarkBakeFailure(
                spawner,
                mobCount,
                poolCount,
                0,
                "no walkable cells near spawner — rebake walk surface (--objects-only or --convert-chunks --force)");
            return 0;
        }

        var origin = spawner.GlobalPosition;
        var validated = new List<Vector3>(poolCount);
        OutdoorSpawnSlotValidator.FailReason? lastFailure = null;
        var searchRadiusUsed = spawner.SpawnRadiusMeters;
        foreach (var searchRadius in BuildSearchRadii(spawner))
        {
            searchRadiusUsed = searchRadius;
            if (!WalkSurfaceWalkableQuery.TryPickSpawnSlots(
                    origin,
                    searchRadius,
                    poolCount * 4,
                    OutdoorFieldConfig.MinSlotSeparationMeters,
                    existingOccupied: null,
                    out var rawCandidates))
            {
                rawCandidates = [];
            }

            Shuffle(rawCandidates);
            validated.Clear();
            var picked = new List<Vector3>();
            foreach (var (x, z, _) in rawCandidates)
            {
                if (validated.Count >= poolCount)
                {
                    break;
                }

                if (!MonsterSpawnGroundQuery.TryResolveSpawnGroundY(spawner, x, z, out var groundY))
                {
                    lastFailure = OutdoorSpawnSlotValidator.FailReason.NotWalkable;
                    continue;
                }

                var candidate = new Vector3(x, groundY, z);
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

            if (validated.Count >= poolCount)
            {
                break;
            }
        }

        if (validated.Count < mobCount)
        {
            var detail = lastFailure?.ToString() ?? "insufficient walkable candidates";
            if (searchRadiusUsed > spawner.SpawnRadiusMeters + 0.01f)
            {
                detail += $" (searched up to {searchRadiusUsed:0.##}m radius)";
            }

            MarkBakeFailure(spawner, mobCount, poolCount, validated.Count, detail);
        }
        else
        {
            spawner.ClearSpawnError();
            if (validated.Count < poolCount)
            {
                GD.Print(
                    $"MonsterSpawnSlotBaker: spawner '{spawner.Name}' baked {validated.Count}/{poolCount} pool slot(s) "
                    + $"(enough for {mobCount} mob(s)).");
            }
        }

        spawner.SetBakedSpawnSlots(validated);
        return validated.Count;
    }

    private static IEnumerable<float> BuildSearchRadii(MonsterSpawner spawner)
    {
        var configured = spawner.SpawnRadiusMeters;
        var expanded = Mathf.Min(configured * 2.5f, spawner.LeashRadiusMeters);
        var leash = spawner.LeashRadiusMeters;

        yield return configured;
        if (expanded > configured + 0.01f)
        {
            yield return expanded;
        }

        if (leash > expanded + 0.01f)
        {
            yield return leash;
        }
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

    private static void MarkBakeFailure(
        MonsterSpawner spawner,
        int mobCount,
        int poolCount,
        int foundCount,
        string detail)
    {
        GD.PushWarning(
            $"MonsterSpawnSlotBaker: spawner '{spawner.Name}' at {spawner.GlobalPosition}: "
            + $"found {foundCount}/{poolCount} pool slot(s), need {mobCount} mob(s) ({detail}).");
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
