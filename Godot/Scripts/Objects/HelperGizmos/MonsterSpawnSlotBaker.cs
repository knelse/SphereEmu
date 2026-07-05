using System.Collections.Generic;
using System.Threading.Tasks;
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
    private const int MaxLooseAttemptsMultiplier = 12;

    public static int BakeForSpawner(MonsterSpawner spawner)
    {
        var mobCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
        if (mobCount <= 0)
        {
            spawner.ClearSpawnError();
            spawner.SetBakedSpawnSlots([]);
            return 0;
        }

        var job = CreateBakeParams(spawner);
        PreloadCachesForJob(job);
        var result = BakeCore(job);
        return ApplyBakeResult(spawner, job, result);
    }

    public static int BakeAllUnder(Node parent)
    {
        var work = new List<(MonsterSpawner Spawner, SpawnerBakeParams Job)>();
        foreach (var child in parent.GetChildren())
        {
            if (child is not MonsterSpawner spawner || !GodotObject.IsInstanceValid(spawner))
            {
                continue;
            }

            var mobCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
            if (mobCount <= 0)
            {
                spawner.ClearSpawnError();
                spawner.SetBakedSpawnSlots([]);
                continue;
            }

            work.Add((spawner, CreateBakeParams(spawner)));
        }

        if (work.Count == 0)
        {
            GD.Print("MonsterSpawnSlotBaker: no spawners to bake.");
            return 0;
        }

        if (!WalkSurfaceCache.HasAnyChunkFiles())
        {
            foreach (var (spawner, job) in work)
            {
                ApplyBakeResult(
                    spawner,
                    job,
                    new SpawnerBakeResult { FailureDetail = "no walk atlas" });
            }

            GD.Print($"MonsterSpawnSlotBaker: baked 0 slot(s) across {work.Count} spawner(s), {work.Count} ERROR.");
            return 0;
        }

        foreach (var (_, job) in work)
        {
            PreloadCachesForJob(job);
        }

        var results = new SpawnerBakeResult[work.Count];
        Parallel.For(0, work.Count, index => results[index] = BakeCore(work[index].Job));

        var slotCount = 0;
        var errors = 0;
        for (var i = 0; i < work.Count; i++)
        {
            slotCount += ApplyBakeResult(work[i].Spawner, work[i].Job, results[i]);
            if (work[i].Spawner.HasSpawnError)
            {
                errors++;
            }
        }

        GD.Print($"MonsterSpawnSlotBaker: baked {slotCount} slot(s) across {work.Count} spawner(s), {errors} ERROR.");
        return slotCount;
    }

    internal static SpawnerBakeResult BakeCore(SpawnerBakeParams job)
    {
        if (!WalkSurfaceCache.HasAnyChunkFiles())
        {
            return new SpawnerBakeResult { FailureDetail = "no walk atlas" };
        }

        if (!WalkSurfaceCache.HasWalkableField)
        {
            return new SpawnerBakeResult
            {
                FailureDetail =
                    "no walkable cells near spawner — rebake walk surface (--objects-only or --convert-chunks --force)",
            };
        }

        var bakeContext = SpawnSlotBakeContext.Create(job.Origin, job.SpawnRadiusMeters);
        var validated = new List<Vector3>(job.PoolCount);
        OutdoorSpawnSlotValidator.FailReason? lastFailure = null;
        var picked = new List<Vector3>();

        TryAddAtlasCandidates(job, bakeContext, validated, picked, ref lastFailure);
        if (validated.Count < job.MobCount)
        {
            TryAddLooseTerrainCandidates(job, bakeContext, validated, picked, ref lastFailure);
        }

        if (validated.Count < job.MobCount)
        {
            var detail = lastFailure?.ToString() ?? "insufficient walkable candidates";
            detail += $" within {job.SpawnRadiusMeters:0.##}m spawn radius";
            return new SpawnerBakeResult
            {
                FoundCount = validated.Count,
                FailureDetail = detail,
            };
        }

        return new SpawnerBakeResult
        {
            Success = true,
            FoundCount = validated.Count,
            Slots = validated,
        };
    }

    private static int ApplyBakeResult(MonsterSpawner spawner, SpawnerBakeParams job, SpawnerBakeResult result)
    {
        if (!result.Success)
        {
            MarkBakeFailure(
                spawner,
                job.MobCount,
                job.PoolCount,
                result.FoundCount,
                result.FailureDetail ?? "insufficient walkable candidates");
            return 0;
        }

        RefineSlotGroundY(spawner, result.Slots);

        spawner.ClearSpawnError();
        if (result.FoundCount < job.PoolCount)
        {
            GD.Print(
                $"MonsterSpawnSlotBaker: spawner '{spawner.Name}' baked {result.FoundCount}/{job.PoolCount} pool slot(s) "
                + $"(enough for {job.MobCount} mob(s), radius {job.SpawnRadiusMeters:0.##}m).");
        }

        spawner.SetBakedSpawnSlots(result.Slots);
        return result.FoundCount;
    }

    private static void RefineSlotGroundY(MonsterSpawner spawner, List<Vector3> slots)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (MonsterSpawnGroundQuery.TryResolveSpawnGroundYForBake(spawner, slot.X, slot.Z, out var groundY))
            {
                slots[i] = new Vector3(slot.X, groundY, slot.Z);
            }
        }
    }

    private static SpawnerBakeParams CreateBakeParams(MonsterSpawner spawner)
    {
        var mobCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
        return new SpawnerBakeParams
        {
            Origin = spawner.GlobalPosition,
            SpawnRadiusMeters = spawner.SpawnRadiusMeters,
            LeashRadiusMeters = spawner.LeashRadiusMeters,
            MobCount = mobCount,
            PoolCount = OutdoorFieldConfig.ComputeBakedSlotPoolCount(mobCount, spawner.SpawnRadiusMeters),
        };
    }

    private static void PreloadCachesForJob(SpawnerBakeParams job)
    {
        var preloadRadius = job.SpawnRadiusMeters + 1f;
        WalkSurfaceCache.PreloadChunksForRadius(job.Origin.X, job.Origin.Z, preloadRadius);
        if (OutdoorNavCache.HasAnyNavFiles())
        {
            OutdoorNavCache.PreloadForRadius(job.Origin.X, job.Origin.Z, preloadRadius);
        }
    }

    private static void TryAddAtlasCandidates(
        SpawnerBakeParams job,
        SpawnSlotBakeContext bakeContext,
        List<Vector3> validated,
        List<Vector3> picked,
        ref OutdoorSpawnSlotValidator.FailReason? lastFailure)
    {
        if (!WalkSurfaceWalkableQuery.TryPickSpawnSlots(
                job.Origin,
                job.SpawnRadiusMeters,
                job.PoolCount * 4,
                OutdoorFieldConfig.MinSlotSeparationMeters,
                existingOccupied: null,
                out var rawCandidates))
        {
            rawCandidates = [];
        }

        Shuffle(rawCandidates);
        foreach (var (x, z, _) in rawCandidates)
        {
            if (validated.Count >= job.PoolCount)
            {
                break;
            }

            TryAddCandidate(job, x, z, validated, picked, bakeContext, OutdoorSpawnSlotValidator.ValidationMode.AtlasFootprint, ref lastFailure);
        }
    }

    private static void TryAddLooseTerrainCandidates(
        SpawnerBakeParams job,
        SpawnSlotBakeContext bakeContext,
        List<Vector3> validated,
        List<Vector3> picked,
        ref OutdoorSpawnSlotValidator.FailReason? lastFailure)
    {
        var samples = CollectLooseSamplesNearSpawner(job.Origin, job.SpawnRadiusMeters, bakeContext);

        var maxAttempts = Mathf.Min(
            samples.Count,
            Mathf.Max(job.MobCount, job.PoolCount) * MaxLooseAttemptsMultiplier);
        for (var attempt = 0; attempt < maxAttempts && validated.Count < job.PoolCount; attempt++)
        {
            var (x, z) = samples[attempt];
            TryAddCandidate(job, x, z, validated, picked, bakeContext, OutdoorSpawnSlotValidator.ValidationMode.LooseTerrain, ref lastFailure);
        }
    }

    private static List<(float X, float Z)> CollectLooseSamplesNearSpawner(
        Vector3 origin,
        float spawnRadius,
        SpawnSlotBakeContext bakeContext)
    {
        var samples = new List<(float X, float Z)>();
        var seen = new HashSet<(int, int)>();
        AddLooseSamples(origin.X, origin.Z, spawnRadius, origin, spawnRadius, samples, seen);
        if (bakeContext.HasWalkAnchor)
        {
            AddLooseSamples(
                bakeContext.WalkAnchor.X,
                bakeContext.WalkAnchor.Z,
                spawnRadius,
                origin,
                spawnRadius,
                samples,
                seen);
        }

        Shuffle(samples);
        return samples;
    }

    private static void AddLooseSamples(
        float centerX,
        float centerZ,
        float collectRadius,
        Vector3 spawnerOrigin,
        float spawnRadius,
        List<(float X, float Z)> destination,
        HashSet<(int, int)> seen)
    {
        var scratch = new List<(float X, float Z)>();
        WalkSurfaceCache.CollectLooseWalkSamplesInRadius(centerX, centerZ, collectRadius, scratch);
        var radiusSq = spawnRadius * spawnRadius;
        foreach (var (x, z) in scratch)
        {
            var dx = x - spawnerOrigin.X;
            var dz = z - spawnerOrigin.Z;
            if (dx * dx + dz * dz > radiusSq)
            {
                continue;
            }

            var key = ((int)Mathf.Round(x * 4f), (int)Mathf.Round(z * 4f));
            if (!seen.Add(key))
            {
                continue;
            }

            destination.Add((x, z));
        }
    }

    private static void TryAddCandidate(
        SpawnerBakeParams job,
        float x,
        float z,
        List<Vector3> validated,
        List<Vector3> picked,
        SpawnSlotBakeContext bakeContext,
        OutdoorSpawnSlotValidator.ValidationMode mode,
        ref OutdoorSpawnSlotValidator.FailReason? lastFailure)
    {
        if (!MonsterSpawnGroundQuery.TryResolveSpawnGroundYFromAtlas(x, z, out var groundY))
        {
            lastFailure = OutdoorSpawnSlotValidator.FailReason.NotWalkable;
            return;
        }

        var candidate = new Vector3(x, groundY, z);
        if (!IsSeparated(candidate, picked, OutdoorFieldConfig.MinSlotSeparationMeters))
        {
            return;
        }

        if (!OutdoorSpawnSlotValidator.TryValidateCandidate(
                job.Origin,
                job.SpawnRadiusMeters,
                job.LeashRadiusMeters,
                candidate,
                out var reason,
                mode,
                bakeContext))
        {
            lastFailure = reason;
            return;
        }

        validated.Add(candidate);
        picked.Add(candidate);
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

    private static void Shuffle(List<(float X, float Z)> candidates)
    {
        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
    }
}
