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
///     <see cref="TerrainNavMeshRuntime" />). Candidate generation still samples the walk-surface atlas (cheap
///     raster of the whole map, good for spreading candidates); the navmesh is only the walkability authority.
/// </summary>
public static class MonsterSpawnSlotBaker
{

    /// <summary>
    ///     Synchronous entry point for callers that cannot await (the runtime respawn path in
    ///     <c>MonsterSpawner.DeleteAndRespawnAllMobs</c>, which holds a lock). Registers this spawner's tiles
    ///     with <see cref="TerrainNavMeshRuntime" /> but does not wait out the physics-frame sync those tiles
    ///     may need - by the time gameplay runs this path, the spawner has almost always already been baked
    ///     once via <see cref="BakeForSpawnerAsync" /> (editor time), so its tiles are already loaded and
    ///     synced. If they somehow aren't yet, this just finds fewer/no candidates and reports the existing
    ///     "insufficient walkable candidates" failure instead of crashing (see
    ///     <see cref="TerrainNavMeshRuntime.IsReadyForQueries" />).
    /// </summary>
    public static int BakeForSpawner(MonsterSpawner spawner)
    {
        var job = CreateBakeParamsOrNull(spawner);
        if (job is null)
        {
            return 0;
        }

        PreloadCachesForJob(spawner, job.Value);
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

        var result = BakeCore(job.Value);
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

        if (!WalkSurfaceCache.HasAnyChunkFiles() || !TerrainNavMeshRuntime.HasAnyTileFiles())
        {
            var missing = !WalkSurfaceCache.HasAnyChunkFiles() ? "no walk atlas" : "no navigation mesh tiles baked";
            foreach (var (spawner, job) in work)
            {
                ApplyBakeResult(spawner, job, new SpawnerBakeResult { FailureDetail = missing });
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

        // Pass 2: validate/spread/pick against the now-synced navmesh map.
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

        if (!TerrainNavMeshRuntime.HasAnyTileFiles())
        {
            return new SpawnerBakeResult { FailureDetail = "no navigation mesh tiles baked" };
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
        // The atlas ground-Y lookup is only a placeholder here: OutdoorSpawnSlotValidator ignores
        // candidate.Y entirely (it probes the navmesh at spawnerOrigin.Y and returns the navmesh's own
        // snapped Y on success), so a missing/holey atlas sample must never block a candidate from
        // reaching the navmesh check - the navmesh, not the atlas, is the walkability authority here.
        // Falling back to the spawner's own Y keeps the placeholder harmless either way.
        var groundY = TryResolveCandidateGroundY(x, z, out var atlasGroundY) ? atlasGroundY : job.Origin.Y;
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
                out var refinedCandidate,
                out var reason))
        {
            lastFailure = reason;
            return;
        }

        validated.Add(refinedCandidate);
        picked.Add(refinedCandidate);
    }

    private static bool TryResolveCandidateGroundY(float x, float z, out float groundY)
    {
        if (WalkSurfaceCache.TrySampleWalkableGround(x, z, out groundY) && !float.IsNaN(groundY))
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
