using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Navigation;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Pre-bakes validated outdoor spawn slots against the baked navmesh (see
///     <see cref="TerrainNavMeshRuntime" />). The navmesh is the sole walkability/height authority.
///     Candidate XZ seeding uses a loose grid (and optionally the walk-surface atlas when present for denser
///     spread); atlas ground height is never used for placement.
/// </summary>
public static class MonsterSpawnSlotBaker
{

    /// <summary>
    ///     Synchronous entry point for callers that cannot await (the runtime respawn path in
    ///     <c>MonsterSpawner.DeleteAndRespawnAllMobs</c>, which holds a lock). Registers this spawner's tiles
    ///     and force-flushes the nav map via <see cref="TerrainNavMeshRuntime.TrySyncImmediate" /> (no physics
    ///     frame wait under the lock). Prefer <see cref="BakeForSpawnerAsync" /> from editor tools.
    /// </summary>
    public static int BakeForSpawner(MonsterSpawner spawner)
    {
        var job = CreateBakeParamsOrNull(spawner);
        if (job is null)
        {
            return 0;
        }

        PreloadCachesForJob(spawner, job.Value);
        TerrainNavMeshRuntime.EnsureTilesLoaded(spawner, job.Value.Origin, job.Value.SpawnRadiusMeters + 1f);
        // Cannot await physics frames under the respawn lock - force-flush instead.
        TerrainNavMeshRuntime.TrySyncImmediate();
        var result = BakeCore(job.Value);
        return ApplyBakeResult(spawner, job.Value, result);
    }

    /// <summary>
    ///     Preferred entry point: preloads walk/nav caches and navmesh tiles, awaits the navmesh sync, then
    ///     bakes. Used by the "Bake spawn slots" editor tool button, which can afford to await a frame.
    /// </summary>
    public static async Task<int> BakeForSpawnerAsync(MonsterSpawner spawner)
    {
        var job = CreateBakeParamsOrNull(spawner);
        if (job is null)
        {
            return 0;
        }

        PreloadCachesForJob(spawner, job.Value);
        TerrainNavMeshRuntime.EnsureTilesLoaded(spawner, job.Value.Origin, job.Value.SpawnRadiusMeters + 1f);

        var tree = spawner.GetTree();
        if (tree is not null)
        {
            await TerrainNavMeshRuntime.SyncAsync(tree);
        }
        else
        {
            TerrainNavMeshRuntime.TrySyncImmediate();
        }

        var result = BakeCore(job.Value);
        // One retry: a lost race with nav map sync used to look like a permanently invalid spawner.
        if (!result.Success && tree is not null && LooksLikeTransientNavSyncFailure(result))
        {
            TerrainNavMeshRuntime.EnsureTilesLoaded(spawner, job.Value.Origin, job.Value.SpawnRadiusMeters + 1f);
            await TerrainNavMeshRuntime.SyncAsync(tree, force: true);
            result = BakeCore(job.Value);
        }

        return ApplyBakeResult(spawner, job.Value, result);
    }

    /// <summary>
    ///     Batch entry point for "Bake spawn slots on all spawners". Two-pass: registers every spawner's
    ///     navmesh tiles first, syncs once for the whole batch, then validates/spreads/picks in parallel -
    ///     far cheaper than syncing once per spawner.
    /// </summary>
    public static async Task<int> BakeAllUnderAsync(Node parent)
    {
        var work = new List<(MonsterSpawner Spawner, SpawnerBakeParams Job)>();
        foreach (var child in parent.GetChildren())
        {
            if (child is not MonsterSpawner spawner || !GodotObject.IsInstanceValid(spawner))
            {
                continue;
            }

            var job = CreateBakeParamsOrNull(spawner);
            if (job is not null)
            {
                work.Add((spawner, job.Value));
            }
        }

        if (work.Count == 0)
        {
            GD.Print("MonsterSpawnSlotBaker: no spawners to bake.");
            return 0;
        }

        if (!TerrainNavMeshRuntime.HasAnyTileFiles())
        {
            foreach (var (spawner, job) in work)
            {
                ApplyBakeResult(spawner, job, new SpawnerBakeResult { FailureDetail = "no navigation mesh tiles baked" });
            }

            GD.Print($"MonsterSpawnSlotBaker: baked 0 slot(s) across {work.Count} spawner(s), {work.Count} ERROR.");
            return 0;
        }

        // Pass 1: preload walk/nav caches and register every needed navmesh tile for every spawner (no queries yet).
        foreach (var (spawner, job) in work)
        {
            PreloadCachesForJob(spawner, job);
            TerrainNavMeshRuntime.EnsureTilesLoaded(spawner, job.Origin, job.SpawnRadiusMeters + 1f);
        }

        // One sync for the whole batch instead of one per spawner.
        var tree = parent.GetTree();
        if (tree is not null)
        {
            await TerrainNavMeshRuntime.SyncAsync(tree);
        }
        else
        {
            TerrainNavMeshRuntime.TrySyncImmediate();
        }

        // Pass 2: validate/spread/pick against the now-synced navmesh map.
        var results = new SpawnerBakeResult[work.Count];
        Parallel.For(0, work.Count, index => results[index] = BakeCore(work[index].Job));

        // Retry only the spawners that look like a transient nav-sync miss (same spawner often succeeds
        // on a second bake once the map has finished synchronizing).
        if (tree is not null)
        {
            var retryIndexes = new List<int>();
            for (var i = 0; i < results.Length; i++)
            {
                if (!results[i].Success && LooksLikeTransientNavSyncFailure(results[i]))
                {
                    retryIndexes.Add(i);
                }
            }

            if (retryIndexes.Count > 0)
            {
                foreach (var index in retryIndexes)
                {
                    var (spawner, job) = work[index];
                    TerrainNavMeshRuntime.EnsureTilesLoaded(spawner, job.Origin, job.SpawnRadiusMeters + 1f);
                }

                await TerrainNavMeshRuntime.SyncAsync(tree, force: true);
                Parallel.For(0, retryIndexes.Count, r =>
                {
                    var index = retryIndexes[r];
                    results[index] = BakeCore(work[index].Job);
                });
            }
        }

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

    private static bool LooksLikeTransientNavSyncFailure(SpawnerBakeResult result)
    {
        var detail = result.FailureDetail ?? string.Empty;
        // Total wipeout NotWalkable is the sync-race signature; partial fills are real geometry limits.
        return detail.Contains("navigation map not synced", StringComparison.Ordinal)
               || (result.FoundCount == 0
                   && detail.Contains(
                       nameof(OutdoorSpawnSlotValidator.FailReason.NotWalkable),
                       StringComparison.Ordinal));
    }

    internal static SpawnerBakeResult BakeCore(SpawnerBakeParams job)
    {
        if (!TerrainNavMeshRuntime.HasAnyTileFiles())
        {
            return new SpawnerBakeResult { FailureDetail = "no navigation mesh tiles baked" };
        }

        if (!TerrainNavMeshRuntime.IsReadyForQueries)
        {
            return new SpawnerBakeResult
            {
                FailureDetail =
                    "navigation map not synced yet (transient — retry bake; not an invalid spawner)",
            };
        }

        var bakeContext = SpawnSlotBakeContext.Create(job.Origin, job.SpawnRadiusMeters);
        var validated = new List<Vector3>(job.PoolCount);
        OutdoorSpawnSlotValidator.FailReason? lastFailure = null;
        var picked = new List<Vector3>();

        TryFillPoolWithSpread(job, bakeContext, validated, picked, ref lastFailure);

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

    private static SpawnerBakeParams? CreateBakeParamsOrNull(MonsterSpawner spawner)
    {
        var mobCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
        if (mobCount <= 0)
        {
            spawner.ClearSpawnError();
            spawner.SetBakedSpawnSlots([]);
            return null;
        }

        var origin = spawner.GlobalPosition;
        var spawnRadius = spawner.SpawnRadiusMeters;
        return new SpawnerBakeParams
        {
            Origin = origin,
            SpawnRadiusMeters = spawnRadius,
            LeashRadiusMeters = spawner.LeashRadiusMeters,
            MobCount = mobCount,
            PoolCount = OutdoorFieldConfig.ComputeBakedSlotPoolCount(mobCount, spawnRadius),
        };
    }

    private static void PreloadCachesForJob(MonsterSpawner spawner, SpawnerBakeParams job)
    {
        var preloadRadius = job.SpawnRadiusMeters + 1f;
        if (WalkSurfaceCache.HasAnyChunkFiles())
        {
            WalkSurfaceCache.PreloadChunksForRadius(job.Origin.X, job.Origin.Z, preloadRadius);
        }

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
        ref OutdoorSpawnSlotValidator.FailReason? lastFailure)
    {
        var samples = BuildSpreadSamplePool(job, bakeContext);
        while (validated.Count < job.PoolCount && samples.Count > 0)
        {
            var bestIndex = FindFarthestSampleIndex(job.Origin, samples, picked);
            var (x, z) = samples[bestIndex];
            samples.RemoveAt(bestIndex);
            TryAddCandidate(job, x, z, validated, picked, ref lastFailure);
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

        if (WalkSurfaceCache.HasAnyChunkFiles())
        {
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
        }

        if (bakeContext.HasWalkAnchor)
        {
            if (WalkSurfaceCache.HasAnyChunkFiles())
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
        ref OutdoorSpawnSlotValidator.FailReason? lastFailure)
    {
        // Placeholder Y only: OutdoorSpawnSlotValidator ignores candidate.Y and probes the navmesh at
        // spawnerOrigin.Y, returning the navmesh-snapped Y on success.
        var candidate = new Vector3(x, job.Origin.Y, z);
        if (!IsSeparated(candidate, picked, OutdoorFieldConfig.MinSlotSeparationMeters))
        {
            return;
        }

        if (!OutdoorSpawnSlotValidator.TryValidateCandidate(
                job.Origin,
                job.SpawnRadiusMeters,
                job.LeashRadiusMeters,
                candidate,
                out var refinedCandidate,
                out var reason))
        {
            lastFailure = reason;
            return;
        }

        validated.Add(refinedCandidate);
        picked.Add(refinedCandidate);
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
