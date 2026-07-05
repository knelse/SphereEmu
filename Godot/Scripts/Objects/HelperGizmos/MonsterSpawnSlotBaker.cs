using System;
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
    public static int BakeForSpawner(MonsterSpawner spawner)
    {
        var mobCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
        if (mobCount <= 0)
        {
            spawner.ClearSpawnError();
            spawner.SetBakedSpawnSlots([]);
            return 0;
        }

        var job = CreateBakeParams(spawner, captureTerrainMesh: true);
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

            work.Add((spawner, CreateBakeParams(spawner, captureTerrainMesh: true)));
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

        var bakeContext = SpawnSlotBakeContext.Create(job.Origin, job.SpawnRadiusMeters, job.TerrainHeights);
        var validated = new List<Vector3>(job.PoolCount);
        OutdoorSpawnSlotValidator.FailReason? lastFailure = null;
        var picked = new List<Vector3>();

        TryFillPoolWithSpread(job, bakeContext, validated, picked, OutdoorSpawnSlotValidator.ValidationMode.LooseTerrain, ref lastFailure);
        if (validated.Count < job.PoolCount)
        {
            TryFillPoolWithSpread(job, bakeContext, validated, picked, OutdoorSpawnSlotValidator.ValidationMode.AtlasFootprint, ref lastFailure);
        }

        if (validated.Count < job.PoolCount && job.TerrainHeights is not null)
        {
            TryFillPoolWithSpread(job, bakeContext, validated, picked, OutdoorSpawnSlotValidator.ValidationMode.TerrainMesh, ref lastFailure);
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

    private static SpawnerBakeParams CreateBakeParams(MonsterSpawner spawner, bool captureTerrainMesh = false)
    {
        var mobCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
        var origin = spawner.GlobalPosition;
        var spawnRadius = spawner.SpawnRadiusMeters;
        return new SpawnerBakeParams
        {
            Origin = origin,
            SpawnRadiusMeters = spawnRadius,
            LeashRadiusMeters = spawner.LeashRadiusMeters,
            MobCount = mobCount,
            PoolCount = OutdoorFieldConfig.ComputeBakedSlotPoolCount(mobCount, spawnRadius),
            TerrainHeights = captureTerrainMesh
                ? TerrainMeshHeightSnapshot.TryCapture(spawner, origin.X, origin.Z, spawnRadius)
                : null,
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

    private static void TryFillPoolWithSpread(
        SpawnerBakeParams job,
        SpawnSlotBakeContext bakeContext,
        List<Vector3> validated,
        List<Vector3> picked,
        OutdoorSpawnSlotValidator.ValidationMode mode,
        ref OutdoorSpawnSlotValidator.FailReason? lastFailure)
    {
        var samples = BuildSpreadSamplePool(job, bakeContext);
        while (validated.Count < job.PoolCount && samples.Count > 0)
        {
            var bestIndex = FindFarthestSampleIndex(job.Origin, samples, picked);
            var (x, z) = samples[bestIndex];
            samples.RemoveAt(bestIndex);
            TryAddCandidate(job, x, z, validated, picked, bakeContext, mode, ref lastFailure);
        }
    }

    private static List<(float X, float Z)> BuildSpreadSamplePool(
        SpawnerBakeParams job,
        SpawnSlotBakeContext bakeContext)
    {
        var samples = new List<(float X, float Z)>();
        var seen = new HashSet<(int, int)>();
        var radiusSq = job.SpawnRadiusMeters * job.SpawnRadiusMeters;

        AddLooseGridSamples(job.Origin.X, job.Origin.Z, job.SpawnRadiusMeters, job.Origin, radiusSq, samples, seen);

        var atlasCandidates = new List<(float X, float Z, float Y)>();
        WalkSurfaceCache.CollectWalkableCandidates(
            job.Origin.X,
            job.Origin.Z,
            job.SpawnRadiusMeters,
            atlasCandidates);
        foreach (var (x, z, _) in atlasCandidates)
        {
            AddSample(x, z, job.Origin, radiusSq, samples, seen);
        }

        if (bakeContext.HasWalkAnchor)
        {
            var anchorCandidates = new List<(float X, float Z, float Y)>();
            WalkSurfaceCache.CollectWalkableCandidates(
                bakeContext.WalkAnchor.X,
                bakeContext.WalkAnchor.Z,
                job.SpawnRadiusMeters,
                anchorCandidates);
            foreach (var (x, z, _) in anchorCandidates)
            {
                AddSample(x, z, job.Origin, radiusSq, samples, seen);
            }

            AddLooseGridSamples(
                bakeContext.WalkAnchor.X,
                bakeContext.WalkAnchor.Z,
                job.SpawnRadiusMeters,
                job.Origin,
                radiusSq,
                samples,
                seen);
        }

        if (job.TerrainHeights is not null)
        {
            var meshSamples = new List<(float X, float Z, float Y)>();
            job.TerrainHeights.CollectSamplesInRadius(
                job.Origin.X,
                job.Origin.Z,
                job.SpawnRadiusMeters,
                meshSamples);
            foreach (var (x, z, _) in meshSamples)
            {
                AddSample(x, z, job.Origin, radiusSq, samples, seen);
            }
        }

        return samples;
    }

    private static void AddLooseGridSamples(
        float centerX,
        float centerZ,
        float collectRadius,
        Vector3 spawnerOrigin,
        float spawnRadiusSq,
        List<(float X, float Z)> samples,
        HashSet<(int, int)> seen)
    {
        var scratch = new List<(float X, float Z)>();
        WalkSurfaceCache.CollectLooseWalkSamplesInRadius(
            centerX,
            centerZ,
            collectRadius,
            scratch,
            sampleSpacingMeters: OutdoorFieldConfig.MinSlotSeparationMeters,
            requireLooseWalk: false);
        foreach (var (x, z) in scratch)
        {
            AddSample(x, z, spawnerOrigin, spawnRadiusSq, samples, seen);
        }
    }

    private static void AddSample(
        float x,
        float z,
        Vector3 spawnerOrigin,
        float spawnRadiusSq,
        List<(float X, float Z)> samples,
        HashSet<(int, int)> seen)
    {
        var dx = x - spawnerOrigin.X;
        var dz = z - spawnerOrigin.Z;
        if (dx * dx + dz * dz > spawnRadiusSq)
        {
            return;
        }

        var key = ((int)Mathf.Round(x * 4f), (int)Mathf.Round(z * 4f));
        if (!seen.Add(key))
        {
            return;
        }

        samples.Add((x, z));
    }

    private static int FindFarthestSampleIndex(
        Vector3 origin,
        IReadOnlyList<(float X, float Z)> samples,
        IReadOnlyList<Vector3> picked)
    {
        var bestIndex = 0;
        var bestScore = float.MinValue;
        for (var i = 0; i < samples.Count; i++)
        {
            var (x, z) = samples[i];
            var score = MinDistanceScore(origin, picked, x, z);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static float MinDistanceScore(Vector3 origin, IReadOnlyList<Vector3> picked, float x, float z)
    {
        var minDistSq = DistanceSq(origin.X, origin.Z, x, z);
        foreach (var position in picked)
        {
            minDistSq = Math.Min(minDistSq, DistanceSq(position.X, position.Z, x, z));
        }

        return minDistSq;
    }

    private static float DistanceSq(float ax, float az, float bx, float bz)
    {
        var dx = ax - bx;
        var dz = az - bz;
        return dx * dx + dz * dz;
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
        if (!TryResolveCandidateGroundY(job, x, z, mode, out var groundY))
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

    private static bool TryResolveCandidateGroundY(
        SpawnerBakeParams job,
        float x,
        float z,
        OutdoorSpawnSlotValidator.ValidationMode mode,
        out float groundY)
    {
        if (mode == OutdoorSpawnSlotValidator.ValidationMode.TerrainMesh
            && job.TerrainHeights is not null
            && job.TerrainHeights.TrySample(x, z, out groundY))
        {
            return true;
        }

        if (mode == OutdoorSpawnSlotValidator.ValidationMode.LooseTerrain
            && WalkSurfaceCache.TrySampleGround(x, z, out groundY)
            && !float.IsNaN(groundY))
        {
            return true;
        }

        if (WalkSurfaceCache.TrySampleWalkableGround(x, z, out groundY)
            && !float.IsNaN(groundY))
        {
            return true;
        }

        return WalkSurfaceCache.TrySampleGround(x, z, out groundY) && !float.IsNaN(groundY);
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
}
