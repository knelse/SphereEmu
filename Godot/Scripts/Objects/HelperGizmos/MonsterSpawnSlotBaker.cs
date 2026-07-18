using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <summary>Spatial batch size for full rebakes — keeps NavigationServer from holding the whole map.</summary>
    private const float BatchRegionSizeMeters = 500f;

    /// <summary>Coarser XZ grid for batch rebake candidate seeding (~4× fewer samples than 0.7 m).</summary>
    private const float FastSampleSpacingMeters = 1.4f;

    /// <summary>
    ///     Persist the edited scene every N dirty spawners so a long bake-all can resume via dirty-skip
    ///     after an interrupt/crash.
    /// </summary>
    private const int ProgressSaveEverySpawners = 100;

    /// <summary>
    ///     Synchronous entry point for callers that cannot await (the runtime respawn path in
    ///     <c>MonsterSpawner.DeleteAndRespawnAllMobs</c>, which holds a lock). Registers this spawner's tiles
    ///     and force-flushes the nav map via <see cref="TerrainNavMeshRuntime.TrySyncImmediate" /> (no physics
    ///     frame wait under the lock). Prefer <see cref="BakeForSpawnerAsync" /> from editor tools.
    /// </summary>
    public static int BakeForSpawner(MonsterSpawner spawner)
    {
        var job = CreateBakeParamsOrNull(spawner, fastCandidateGeneration: false);
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
    ///     Always rebakes (ignores dirty-skip) at full query quality.
    /// </summary>
    public static async Task<int> BakeForSpawnerAsync(MonsterSpawner spawner)
    {
        var job = CreateBakeParamsOrNull(spawner, fastCandidateGeneration: false);
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
    ///     Batch entry point for "Bake spawn slots on all spawners". Skips spawners that already have enough
    ///     non-error slots, then bakes the rest in spatial regions (load → sync → bake → unload) with a
    ///     cheaper candidate/nav query path. Failures get one sequential full-quality retry (same path as
    ///     <see cref="BakeForSpawnerAsync" />) after a forced nav sync.
    /// </summary>
    public static Task<int> BakeAllUnderAsync(Node parent)
        => BakeAllUnderAsync(parent, new SpawnSlotBakeAllSettings());

    /// <param name="settings">
    ///     Headless runs should set <see cref="SpawnSlotBakeAllSettings.YieldProcessFrames" /> false and provide
    ///     <see cref="SpawnSlotBakeAllSettings.ProgressFilePath" /> for crash-safe checkpoints.
    /// </param>
    public static async Task<int> BakeAllUnderAsync(Node parent, SpawnSlotBakeAllSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var stopwatch = Stopwatch.StartNew();
        SpawnSlotBakeProgress? progress = null;
        if (!string.IsNullOrEmpty(settings.ProgressFilePath))
        {
            progress = settings.ForceRebake
                ? new SpawnSlotBakeProgress()
                : SpawnSlotBakeProgress.LoadOrCreate(settings.ProgressFilePath);
            if (!settings.ForceRebake)
            {
                progress.ApplyToSpawners(parent);
            }
        }

        var dirty = new List<(MonsterSpawner Spawner, SpawnerBakeParams Job)>();
        var skipped = 0;

        foreach (var child in parent.GetChildren())
        {
            if (child is not MonsterSpawner spawner || !GodotObject.IsInstanceValid(spawner))
            {
                continue;
            }

            var job = CreateBakeParamsOrNull(spawner, fastCandidateGeneration: true);
            if (job is null)
            {
                continue;
            }

            var key = SpawnSlotBakeProgress.GetSpawnerKey(spawner);
            // Sidecar covers both successes and hard failures (e.g. dungeon Y with no outdoor nav).
            if (!settings.ForceRebake && progress is not null && progress.Contains(key))
            {
                skipped++;
                continue;
            }

            if (!settings.ForceRebake && IsAlreadyBaked(spawner, job.Value))
            {
                skipped++;
                continue;
            }

            dirty.Add((spawner, job.Value));
        }

        if (dirty.Count == 0)
        {
            GD.Print(
                $"MonsterSpawnSlotBaker: nothing to bake ({skipped} spawner(s) already have slots). "
                + $"{stopwatch.Elapsed.TotalSeconds:0.0}s");
            return 0;
        }

        if (!TerrainNavMeshRuntime.HasAnyTileFiles())
        {
            foreach (var (spawner, job) in dirty)
            {
                ApplyBakeResult(
                    spawner,
                    job,
                    new SpawnerBakeResult { FailureDetail = "no navigation mesh tiles baked" },
                    progress);
            }

            progress?.Save(settings.ProgressFilePath!);
            GD.Print(
                $"MonsterSpawnSlotBaker: baked 0 slot(s) across {dirty.Count} dirty spawner(s), "
                + $"{dirty.Count} ERROR, skipped {skipped}. {stopwatch.Elapsed.TotalSeconds:0.0}s");
            return 0;
        }

        var regions = GroupByRegion(dirty);
        var tree = parent.GetTree();
        var slotCount = 0;
        var errors = 0;
        var regionIndex = 0;
        var processed = 0;
        var sinceCheckpoint = 0;

        foreach (var regionWork in regions)
        {
            regionIndex++;
            GD.Print(
                $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                + $"loading {regionWork.Count} spawner(s)… ({stopwatch.Elapsed.TotalSeconds:0.0}s)");

            TerrainNavMeshRuntime.UnloadAllRegions();

            foreach (var (spawner, job) in regionWork)
            {
                PreloadCachesForJob(spawner, job);
                TerrainNavMeshRuntime.EnsureTilesLoaded(spawner, job.Origin, job.SpawnRadiusMeters + 1f);
            }

            // Batch path: force-flush only. SyncAsync's frame waits used to hang the editor between
            // regions when PhysicsFrame stopped ticking after the first batch.
            TerrainNavMeshRuntime.TrySyncImmediate(force: true);
            if (settings.YieldProcessFrames && tree is not null)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            var loadedTiles = TerrainNavMeshRuntime.LoadedRegionCount;
            var results = new SpawnerBakeResult[regionWork.Count];

            // Empty / dungeon XZ with no GeneratedNavMeshes tiles: do not run candidate loops — each
            // spawner would burn seconds rejecting every loose-grid sample as NotWalkable.
            if (loadedTiles == 0)
            {
                GD.Print(
                    $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                    + $"0 nav tiles after load; marking {regionWork.Count} spawner(s) ERROR "
                    + $"({stopwatch.Elapsed.TotalSeconds:0.0}s)");
                for (var i = 0; i < regionWork.Count; i++)
                {
                    results[i] = new SpawnerBakeResult
                    {
                        FailureDetail = "no outdoor nav tile coverage at spawner XZ (loaded 0 tiles)",
                    };
                }
            }
            else
            {
                GD.Print(
                    $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                    + $"synced ({loadedTiles} nav tile(s)), baking… ({stopwatch.Elapsed.TotalSeconds:0.0}s)");

                // NavigationServer3D closest-point queries are not safe under Parallel.For — concurrent
                // MapGetClosestPoint calls produce intermittent false NotWalkable that vanish on a sequential
                // manual re-bake of the same spawner.
                for (var i = 0; i < regionWork.Count; i++)
                {
                    results[i] = BakeCore(regionWork[i].Job);
                }

                // Full-quality retry for BakeFast false rejects / sync races. Skip WrongLevel and
                // no-coverage failures (dungeon Y early-out) — those never improve with a denser search.
                var retryIndexes = new List<int>();
                for (var i = 0; i < results.Length; i++)
                {
                    if (ShouldRetryFailureWithFullQuality(results[i]))
                    {
                        retryIndexes.Add(i);
                    }
                }

                if (retryIndexes.Count > 0)
                {
                    foreach (var index in retryIndexes)
                    {
                        var (spawner, job) = regionWork[index];
                        TerrainNavMeshRuntime.EnsureTilesLoaded(spawner, job.Origin, job.SpawnRadiusMeters + 1f);
                    }

                    TerrainNavMeshRuntime.TrySyncImmediate(force: true);

                    foreach (var index in retryIndexes)
                    {
                        var (_, fastJob) = regionWork[index];
                        var fullJob = fastJob with { FastCandidateGeneration = false };
                        results[index] = BakeCore(fullJob);
                    }

                    GD.Print(
                        $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                        + $"full-quality retry for {retryIndexes.Count} failure(s)");
                }
            }

            for (var i = 0; i < regionWork.Count; i++)
            {
                slotCount += ApplyBakeResult(regionWork[i].Spawner, regionWork[i].Job, results[i], progress);
                if (regionWork[i].Spawner.HasSpawnError)
                {
                    errors++;
                }

                processed++;
                sinceCheckpoint++;
                if (sinceCheckpoint >= ProgressSaveEverySpawners)
                {
                    WriteCheckpoint(settings, progress, processed, dirty.Count, stopwatch);
                    sinceCheckpoint = 0;
                }
            }

            GD.Print(
                $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                + $"{regionWork.Count} spawner(s), running total slots={slotCount} errors={errors} "
                + $"({stopwatch.Elapsed.TotalSeconds:0.0}s)");
        }

        TerrainNavMeshRuntime.UnloadAllRegions();

        if (sinceCheckpoint > 0 || progress is not null)
        {
            WriteCheckpoint(settings, progress, processed, dirty.Count, stopwatch);
        }

        GD.Print(
            $"MonsterSpawnSlotBaker: baked {slotCount} slot(s) across {dirty.Count} dirty spawner(s) "
            + $"in {regions.Count} region(s), {errors} ERROR, skipped {skipped} already-baked. "
            + $"{stopwatch.Elapsed.TotalSeconds:0.0}s");
        return slotCount;
    }

    private static void WriteCheckpoint(
        SpawnSlotBakeAllSettings settings,
        SpawnSlotBakeProgress? progress,
        int processed,
        int totalDirty,
        Stopwatch stopwatch)
    {
        if (progress is not null && !string.IsNullOrEmpty(settings.ProgressFilePath))
        {
            progress.Save(settings.ProgressFilePath);
            GD.Print(
                $"MonsterSpawnSlotBaker: wrote progress sidecar "
                + $"({processed}/{totalDirty} dirty, {stopwatch.Elapsed.TotalSeconds:0.0}s)");
        }

        // Do not reference EditorInterface here — that pulls GodotSharpEditor and crashes headless.
        settings.OnCheckpoint?.Invoke(processed, totalDirty, stopwatch);
    }

    private static bool IsAlreadyBaked(MonsterSpawner spawner, SpawnerBakeParams job)
    {
        if (spawner.HasSpawnError || spawner.SpawnPlacementInvalid)
        {
            return false;
        }

        return spawner.BakedSpawnSlots.Count >= job.MobCount;
    }

    private static List<List<(MonsterSpawner Spawner, SpawnerBakeParams Job)>> GroupByRegion(
        List<(MonsterSpawner Spawner, SpawnerBakeParams Job)> dirty)
    {
        var buckets = new Dictionary<(int Rx, int Rz), List<(MonsterSpawner Spawner, SpawnerBakeParams Job)>>();
        foreach (var entry in dirty)
        {
            var key = (
                (int)Mathf.Floor(entry.Job.Origin.X / BatchRegionSizeMeters),
                (int)Mathf.Floor(entry.Job.Origin.Z / BatchRegionSizeMeters));
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add(entry);
        }

        var regions = new List<List<(MonsterSpawner Spawner, SpawnerBakeParams Job)>>(buckets.Count);
        foreach (var list in buckets.Values)
        {
            regions.Add(list);
        }

        // Stable-ish order: west → east, then north → south.
        regions.Sort((a, b) =>
        {
            var ax = a[0].Job.Origin.X;
            var az = a[0].Job.Origin.Z;
            var bx = b[0].Job.Origin.X;
            var bz = b[0].Job.Origin.Z;
            var cmp = ax.CompareTo(bx);
            return cmp != 0 ? cmp : az.CompareTo(bz);
        });

        return regions;
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

    private static bool ShouldRetryFailureWithFullQuality(SpawnerBakeResult result)
    {
        var detail = result.FailureDetail ?? string.Empty;

        // Wrong-level / dungeon early-outs are permanent for this bake.
        if (detail.Contains(nameof(OutdoorSpawnSlotValidator.FailReason.WrongLevel), StringComparison.Ordinal)
            || detail.Contains("no outdoor nav tile coverage", StringComparison.Ordinal))
        {
            return false;
        }

        if (detail.Contains("navigation map not synced", StringComparison.Ordinal))
        {
            return true;
        }

        // Fast batch path under-samples; a full-quality pass often recovers outdoor spawners that
        // looked like a total NotWalkable wipeout on BakeFast.
        return detail.Contains(
                   nameof(OutdoorSpawnSlotValidator.FailReason.NotWalkable),
                   StringComparison.Ordinal)
               || detail.Contains("insufficient walkable candidates", StringComparison.Ordinal);
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

        // Cheap reject before candidate generation: dungeon-layer spawners often sit far below/above
        // outdoor nav at the same XZ. Use raw closest-point (not the 0.2m on-mesh test) so we still
        // catch WrongLevel when the horizontal snap is slightly loose.
        if (TerrainNavMeshRuntime.TryClosestPoint(job.Origin, out var originSnap))
        {
            var maxVerticalDrift = Mathf.Max(
                OutdoorFieldConfig.MinSpawnSlotVerticalDriftMeters,
                job.SpawnRadiusMeters * OutdoorFieldConfig.MaxSpawnSlotVerticalDriftRadiusMultiplier);
            if (Mathf.Abs(originSnap.Y - job.Origin.Y) > maxVerticalDrift)
            {
                return new SpawnerBakeResult
                {
                    FailureDetail =
                        $"{nameof(OutdoorSpawnSlotValidator.FailReason.WrongLevel)} "
                        + $"(nav Y={originSnap.Y:0.##}, spawner Y={job.Origin.Y:0.##})",
                };
            }
        }

        var bakeContext = job.FastCandidateGeneration
            ? default
            : SpawnSlotBakeContext.Create(job.Origin, job.SpawnRadiusMeters);
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

    private static int ApplyBakeResult(
        MonsterSpawner spawner,
        SpawnerBakeParams job,
        SpawnerBakeResult result,
        SpawnSlotBakeProgress? progress = null)
    {
        var key = SpawnSlotBakeProgress.GetSpawnerKey(spawner);
        if (!result.Success)
        {
            MarkBakeFailure(
                spawner,
                job.MobCount,
                job.PoolCount,
                result.FoundCount,
                result.FailureDetail ?? "insufficient walkable candidates");
            progress?.RecordFailure(key);
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
        progress?.RecordSuccess(key, result.Slots);
        return result.FoundCount;
    }

    private static SpawnerBakeParams? CreateBakeParamsOrNull(MonsterSpawner spawner, bool fastCandidateGeneration)
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
            FastCandidateGeneration = fastCandidateGeneration,
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
        var spacing = job.FastCandidateGeneration
            ? FastSampleSpacingMeters
            : OutdoorFieldConfig.MinSlotSeparationMeters;

        AddLooseGridSamples(
            job.Origin.X,
            job.Origin.Z,
            job.SpawnRadiusMeters,
            job.Origin,
            radiusSq,
            spacing,
            samples,
            seen);

        // Batch path: coarse loose grid only. Atlas dual-collect is expensive and redundant with navmesh authority.
        if (job.FastCandidateGeneration)
        {
            return samples;
        }

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
                spacing,
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
        float sampleSpacingMeters,
        List<(float X, float Z)> samples,
        HashSet<(int, int)> seen)
    {
        var scratch = new List<(float X, float Z)>();
        WalkSurfaceCache.CollectLooseWalkSamplesInRadius(
            centerX,
            centerZ,
            collectRadius,
            scratch,
            sampleSpacingMeters: sampleSpacingMeters,
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
                job.FastCandidateGeneration,
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
        // Headless bake-all: region summaries only (thousands of per-spawner lines stall the console).
        if (!MonsterSpawnSlotHeadlessBake.IsActive)
        {
            GD.PushWarning(
                $"MonsterSpawnSlotBaker: spawner '{spawner.Name}' at {spawner.GlobalPosition}: "
                + $"found {foundCount}/{poolCount} pool slot(s), need {mobCount} mob(s) ({detail}).");
        }

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
