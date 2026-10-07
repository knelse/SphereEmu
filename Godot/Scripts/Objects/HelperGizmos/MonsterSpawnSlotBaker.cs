using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Navigation;
using SphServer.Godot.Scripts.Terrain;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
/// Navmesh is the only walkability and height authority. Candidate XZ is a loose grid in the spawn
/// radius (no WalkData)
/// </summary>
public static class MonsterSpawnSlotBaker
{
    /// <summary>
    /// Headless prints per-spawner failures when --name-contains is set
    /// </summary>
    private static bool _verboseHeadlessFailures;

    /// <summary>
    /// Spatial batch size so NavigationServer does not hold the whole map
    /// </summary>
    private const float BatchRegionSizeMeters = 500f;

    /// <summary>
    /// Batch rebake XZ grid, about 4x fewer samples than 0.7 m
    /// </summary>
    private const float FastSampleSpacingMeters = 1.4f;

    /// <summary>
    /// Scene save every N dirty spawners, so an interrupted bake-all resumes via dirty-skip
    /// </summary>
    private const int ProgressSaveEverySpawners = 100;

    /// <summary>
    /// Lock-held respawn cannot await a physics frame
    /// </summary>
    public static int BakeForSpawner (MonsterSpawner spawner)
    {
        var job = CreateBakeParamsOrNull (spawner, fastCandidateGeneration: false);
        if (job is null)
        {
            return 0;
        }

        TerrainNavMeshRuntime.EnsureTilesLoaded (spawner, job.Value.Origin, job.Value.SpawnRadiusMeters + 1f);
        // Respawn holds a lock, so this force-flushes instead of awaiting a physics frame
        TerrainNavMeshRuntime.TrySyncImmediate ();
        var result = BakeCore (job.Value);
        return ApplyBakeResult (spawner, job.Value, result);
    }

    /// <summary>
    /// Always rebakes at full quality, including spawners that already have slots
    /// </summary>
    public static async Task<int> BakeForSpawnerAsync (MonsterSpawner spawner)
    {
        var job = CreateBakeParamsOrNull (spawner, fastCandidateGeneration: false);
        if (job is null)
        {
            return 0;
        }

        TerrainNavMeshRuntime.EnsureTilesLoaded (spawner, job.Value.Origin, job.Value.SpawnRadiusMeters + 1f);

        var tree = spawner.GetTree ();
        if (tree is not null)
        {
            await TerrainNavMeshRuntime.SyncAsync (tree);
        }
        else
        {
            TerrainNavMeshRuntime.TrySyncImmediate ();
        }

        var result = BakeCore (job.Value);
        // A lost race with nav map sync looks like a permanently invalid spawner
        if (!result.Success && tree is not null && LooksLikeTransientNavSyncFailure (result))
        {
            TerrainNavMeshRuntime.EnsureTilesLoaded (spawner, job.Value.Origin, job.Value.SpawnRadiusMeters + 1f);
            await TerrainNavMeshRuntime.SyncAsync (tree, force: true);
            result = BakeCore (job.Value);
        }

        return ApplyBakeResult (spawner, job.Value, result);
    }

    /// <summary>
    /// Skips spawners that already have enough good slots. Failures get one full-quality retry
    /// after a forced nav sync
    /// </summary>
    public static Task<int> BakeAllUnderAsync (Node parent)
        => BakeAllUnderAsync (parent, new SpawnSlotBakeAllSettings ());

    /// <summary>
    /// Headless sets YieldProcessFrames false and a ProgressFilePath so a crash can resume
    /// </summary>
    public static async Task<int> BakeAllUnderAsync (Node parent, SpawnSlotBakeAllSettings settings)
    {
        ArgumentNullException.ThrowIfNull (settings);

        _verboseHeadlessFailures = !string.IsNullOrEmpty (settings.NameContains);
        var stopwatch = Stopwatch.StartNew ();
        SpawnSlotBakeProgress? progress = null;
        if (!string.IsNullOrEmpty (settings.ProgressFilePath))
        {
            progress = settings.ForceRebake
                ? new SpawnSlotBakeProgress ()
                : SpawnSlotBakeProgress.LoadOrCreate (settings.ProgressFilePath);
            if (!settings.ForceRebake)
            {
                progress.ApplyToSpawners (parent);
            }
        }

        var dirty = new List<(MonsterSpawner Spawner, SpawnerBakeParams Job)> ();
        var skipped = 0;

        foreach (var child in parent.GetChildren ())
        {
            if (child is not MonsterSpawner spawner || !GodotObject.IsInstanceValid (spawner))
            {
                continue;
            }

            var key = SpawnSlotBakeProgress.GetSpawnerKey (spawner);
            if (!string.IsNullOrEmpty (settings.NameContains)
                && key.IndexOf (settings.NameContains, StringComparison.OrdinalIgnoreCase) < 0
                && spawner.Name.ToString ().IndexOf (settings.NameContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var job = CreateBakeParamsOrNull (spawner, fastCandidateGeneration: true);
            if (job is null)
            {
                continue;
            }

            // Sidecar records successes and hard failures (a dungeon with no outdoor or indoor nav)
            if (!settings.ForceRebake && progress is not null && progress.Contains (key))
            {
                skipped++;
                continue;
            }

            if (!settings.ForceRebake && IsAlreadyBaked (spawner, job.Value))
            {
                skipped++;
                continue;
            }

            dirty.Add ((spawner, job.Value));
        }

        if (dirty.Count == 0)
        {
            GD.Print (
                $"MonsterSpawnSlotBaker: nothing to bake ({skipped} spawner(s) already have slots). "
                + $"{stopwatch.Elapsed.TotalSeconds:0.0}s");
            return 0;
        }

        if (!TerrainNavMeshRuntime.HasAnyTileFiles ())
        {
            foreach (var (spawner, job) in dirty)
            {
                ApplyBakeResult (
                    spawner,
                    job,
                    new SpawnerBakeResult { FailureDetail = "no navigation mesh tiles baked" },
                    progress);
            }

            progress?.Save (settings.ProgressFilePath!);
            GD.Print (
                $"MonsterSpawnSlotBaker: baked 0 slot(s) across {dirty.Count} dirty spawner(s), "
                + $"{dirty.Count} ERROR, skipped {skipped}. {stopwatch.Elapsed.TotalSeconds:0.0}s");
            return 0;
        }

        var regions = GroupByRegion (dirty);
        var tree = parent.GetTree ();
        var slotCount = 0;
        var errors = 0;
        var regionIndex = 0;
        var processed = 0;
        var sinceCheckpoint = 0;

        foreach (var regionWork in regions)
        {
            regionIndex++;
            GD.Print (
                $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                + $"loading {regionWork.Count} spawner(s)… ({stopwatch.Elapsed.TotalSeconds:0.0}s)");

            TerrainNavMeshRuntime.UnloadAllRegions ();

            foreach (var (spawner, job) in regionWork)
            {
                TerrainNavMeshRuntime.EnsureTilesLoaded (spawner, job.Origin, job.SpawnRadiusMeters + 1f);
            }

            // MapGetClosestPoint needs a physics tick. Force-update alone leaves iteration_id=1 and
            // closest=(0,0,0), which is indoor WrongLevel (nav Y=1)
            if (tree is not null)
            {
                await TerrainNavMeshRuntime.SyncAsync (tree, force: true);
            }
            else
            {
                TerrainNavMeshRuntime.TrySyncImmediate (force: true);
            }

            if (settings.YieldProcessFrames && tree is not null)
            {
                await tree.ToSignal (tree, SceneTree.SignalName.ProcessFrame);
            }

            var loadedTiles = TerrainNavMeshRuntime.LoadedRegionCount;
            var results = new SpawnerBakeResult[regionWork.Count];

            // No outdoor GeneratedNavMeshes and no indoor cluster nav: candidate loops spend
            // seconds rejecting every sample as NotWalkable
            if (loadedTiles == 0)
            {
                GD.Print (
                    $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                    + $"0 nav regions after load; marking {regionWork.Count} spawner(s) ERROR "
                    + $"({stopwatch.Elapsed.TotalSeconds:0.0}s)");
                for (var i = 0; i < regionWork.Count; i++)
                {
                    results[i] = new SpawnerBakeResult
                    {
                        FailureDetail = "no nav coverage at spawner (outdoor tiles + indoor clusters loaded 0)",
                    };
                }
            }
            else
            {
                GD.Print (
                    $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                    + $"synced ({loadedTiles} nav tile(s)), baking… ({stopwatch.Elapsed.TotalSeconds:0.0}s)");

                // NavigationServer3D closest-point is not safe under Parallel.For. Concurrent
                // MapGetClosestPoint calls return false NotWalkable that a sequential rebake does
                // not
                for (var i = 0; i < regionWork.Count; i++)
                {
                    results[i] = BakeCore (regionWork[i].Job);
                }

                // Full-quality retry is for BakeFast false rejects and sync races. WrongLevel and
                // no-coverage (dungeon Y early-out) do not improve with a denser search
                var retryIndexes = new List<int> ();
                for (var i = 0; i < results.Length; i++)
                {
                    if (ShouldRetryFailureWithFullQuality (results[i]))
                    {
                        retryIndexes.Add (i);
                    }
                }

                if (retryIndexes.Count > 0)
                {
                    foreach (var index in retryIndexes)
                    {
                        var (spawner, job) = regionWork[index];
                        TerrainNavMeshRuntime.EnsureTilesLoaded (spawner, job.Origin, job.SpawnRadiusMeters + 1f);
                    }

                    if (tree is not null)
                    {
                        await TerrainNavMeshRuntime.SyncAsync (tree, force: true);
                    }
                    else
                    {
                        TerrainNavMeshRuntime.TrySyncImmediate (force: true);
                    }

                    foreach (var index in retryIndexes)
                    {
                        var (_, fastJob) = regionWork[index];
                        var fullJob = fastJob with { FastCandidateGeneration = false };
                        results[index] = BakeCore (fullJob);
                    }

                    GD.Print (
                        $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                        + $"full-quality retry for {retryIndexes.Count} failure(s)");
                }
            }

            for (var i = 0; i < regionWork.Count; i++)
            {
                slotCount += ApplyBakeResult (regionWork[i].Spawner, regionWork[i].Job, results[i], progress);
                if (regionWork[i].Spawner.HasSpawnError)
                {
                    errors++;
                }

                processed++;
                sinceCheckpoint++;
                if (sinceCheckpoint >= ProgressSaveEverySpawners)
                {
                    WriteCheckpoint (settings, progress, processed, dirty.Count, stopwatch);
                    sinceCheckpoint = 0;
                }
            }

            GD.Print (
                $"MonsterSpawnSlotBaker: region {regionIndex}/{regions.Count} — "
                + $"{regionWork.Count} spawner(s), running total slots={slotCount} errors={errors} "
                + $"({stopwatch.Elapsed.TotalSeconds:0.0}s)");
        }

        TerrainNavMeshRuntime.UnloadAllRegions ();

        if (sinceCheckpoint > 0 || progress is not null)
        {
            WriteCheckpoint (settings, progress, processed, dirty.Count, stopwatch);
        }

        GD.Print (
            $"MonsterSpawnSlotBaker: baked {slotCount} slot(s) across {dirty.Count} dirty spawner(s) "
            + $"in {regions.Count} region(s), {errors} ERROR, skipped {skipped} already-baked. "
            + $"{stopwatch.Elapsed.TotalSeconds:0.0}s");
        return slotCount;
    }

    private static void WriteCheckpoint (
        SpawnSlotBakeAllSettings settings,
        SpawnSlotBakeProgress? progress,
        int processed,
        int totalDirty,
        Stopwatch stopwatch)
    {
        if (progress is not null && !string.IsNullOrEmpty (settings.ProgressFilePath))
        {
            progress.Save (settings.ProgressFilePath);
            GD.Print (
                $"MonsterSpawnSlotBaker: wrote progress sidecar "
                + $"({processed}/{totalDirty} dirty, {stopwatch.Elapsed.TotalSeconds:0.0}s)");
        }

        // EditorInterface pulls GodotSharpEditor and crashes headless
        settings.OnCheckpoint?.Invoke (processed, totalDirty, stopwatch);
    }

    private static bool IsAlreadyBaked (MonsterSpawner spawner, SpawnerBakeParams job)
    {
        if (spawner.HasSpawnError || spawner.SpawnPlacementInvalid)
        {
            return false;
        }

        return spawner.BakedSpawnSlots.Count >= job.MobCount;
    }

    private static List<List<(MonsterSpawner Spawner, SpawnerBakeParams Job)>> GroupByRegion (
        List<(MonsterSpawner Spawner, SpawnerBakeParams Job)> dirty)
    {
        var buckets = new Dictionary<(int Rx, int Rz), List<(MonsterSpawner Spawner, SpawnerBakeParams Job)>> ();
        foreach (var entry in dirty)
        {
            var key = (
                (int) Mathf.Floor (entry.Job.Origin.X / BatchRegionSizeMeters),
                (int) Mathf.Floor (entry.Job.Origin.Z / BatchRegionSizeMeters));
            if (!buckets.TryGetValue (key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add (entry);
        }

        var regions = new List<List<(MonsterSpawner Spawner, SpawnerBakeParams Job)>> (buckets.Count);
        foreach (var list in buckets.Values)
        {
            regions.Add (list);
        }

        // Stable-ish order: west → east, then north → south.
        regions.Sort ((a, b) =>
        {
            var ax = a[0].Job.Origin.X;
            var az = a[0].Job.Origin.Z;
            var bx = b[0].Job.Origin.X;
            var bz = b[0].Job.Origin.Z;
            var cmp = ax.CompareTo (bx);
            return cmp != 0 ? cmp : az.CompareTo (bz);
        });

        return regions;
    }

    private static bool LooksLikeTransientNavSyncFailure (SpawnerBakeResult result)
    {
        var detail = result.FailureDetail ?? string.Empty;
        // Total NotWalkable wipeout is the sync-race signature; a partial fill is a real geometry
        // limit.
        // WrongLevel with nav Y about 0 or 1 is the pre-sync MapGetClosestPoint(Zero) sentinel
        // (indoor remap about Y=1).
        if (detail.Contains (nameof (OutdoorSpawnSlotValidator.FailReason.WrongLevel), StringComparison.Ordinal)
            && (detail.Contains ("nav Y=0", StringComparison.Ordinal)
                || detail.Contains ("nav Y=1", StringComparison.Ordinal)))
        {
            return true;
        }

        return detail.Contains ("navigation map not synced", StringComparison.Ordinal)
               || (result.FoundCount == 0
                   && detail.Contains (
                       nameof (OutdoorSpawnSlotValidator.FailReason.NotWalkable),
                       StringComparison.Ordinal));
    }

    private static bool ShouldRetryFailureWithFullQuality (SpawnerBakeResult result)
    {
        var detail = result.FailureDetail ?? string.Empty;

        // Wrong-level and no-coverage early-outs are permanent for this bake
        if (detail.Contains (nameof (OutdoorSpawnSlotValidator.FailReason.WrongLevel), StringComparison.Ordinal)
            || detail.Contains ("no nav coverage at spawner", StringComparison.Ordinal)
            || detail.Contains ("no outdoor nav tile coverage", StringComparison.Ordinal))
        {
            return false;
        }

        if (detail.Contains ("navigation map not synced", StringComparison.Ordinal))
        {
            return true;
        }

        // BakeFast under-samples. A full-quality pass often recovers outdoor spawners that looked
        // like a total NotWalkable wipeout
        return detail.Contains (
                   nameof (OutdoorSpawnSlotValidator.FailReason.NotWalkable),
                   StringComparison.Ordinal)
               || detail.Contains ("insufficient walkable candidates", StringComparison.Ordinal);
    }

    public static SpawnerBakeResult BakeCore (SpawnerBakeParams job)
    {
        var result = BakeCoreAtOrigin (job);
        if (result.Success)
        {
            return result;
        }

        // Outdoor markers often sit above terrain. If the first pass cannot fill the pool, the
        // center drops onto the navmesh (at most MaxOutdoorDropToNavMeshMeters) and radius is
        // measured from there
        if (!IndoorAreaCriteria.IsIndoorDepth (job.Origin.Y)
            && TerrainNavMeshRuntime.TryFindNavMeshBelow (
                job.Origin,
                OutdoorFieldConfig.MaxOutdoorDropToNavMeshMeters,
                out var groundCenter)
            && job.Origin.Y - groundCenter.Y > 0.05f)
        {
            var dropped = BakeCoreAtOrigin (job with { Origin = groundCenter });
            if (dropped.Success || dropped.FoundCount > result.FoundCount)
            {
                return dropped;
            }
        }

        return result;
    }

    private static SpawnerBakeResult BakeCoreAtOrigin (SpawnerBakeParams job)
    {
        if (!TerrainNavMeshRuntime.HasAnyTileFiles ())
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

        // Closest-point Y drifts far when only outdoor nav is loaded under a dungeon spawner (or
        // the reverse). Indoor probes map through TerrainObjects to grid so dungeon nav can match
        if (TerrainNavMeshRuntime.TryClosestPoint (job.Origin, out var originSnap))
        {
            var maxVerticalDrift = Mathf.Max (
                OutdoorFieldConfig.MinSpawnSlotVerticalDriftMeters,
                job.SpawnRadiusMeters * OutdoorFieldConfig.MaxSpawnSlotVerticalDriftRadiusMultiplier);
            if (Mathf.Abs (originSnap.Y - job.Origin.Y) > maxVerticalDrift)
            {
                return new SpawnerBakeResult
                {
                    FailureDetail =
                        $"{nameof (OutdoorSpawnSlotValidator.FailReason.WrongLevel)} "
                        + $"(nav Y={originSnap.Y:0.##}, spawner Y={job.Origin.Y:0.##})",
                };
            }
        }

        var validated = new List<Vector3> (job.PoolCount);
        OutdoorSpawnSlotValidator.FailReason? lastFailure = null;
        var picked = new List<Vector3> ();

        TryFillPoolWithSpread (job, validated, picked, ref lastFailure);

        if (validated.Count < job.MobCount)
        {
            var detail = lastFailure?.ToString () ?? "insufficient walkable candidates";
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

    private static int ApplyBakeResult (
        MonsterSpawner spawner,
        SpawnerBakeParams job,
        SpawnerBakeResult result,
        SpawnSlotBakeProgress? progress = null)
    {
        var key = SpawnSlotBakeProgress.GetSpawnerKey (spawner);
        if (!result.Success)
        {
            MarkBakeFailure (
                spawner,
                job.MobCount,
                job.PoolCount,
                result.FoundCount,
                result.FailureDetail ?? "insufficient walkable candidates");
            progress?.RecordFailure (key);
            return 0;
        }

        spawner.ClearSpawnError ();
        if (result.FoundCount < job.PoolCount)
        {
            GD.Print (
                $"MonsterSpawnSlotBaker: spawner '{spawner.Name}' baked {result.FoundCount}/{job.PoolCount} pool slot(s) "
                + $"(enough for {job.MobCount} mob(s), radius {job.SpawnRadiusMeters:0.##}m).");
        }

        spawner.SetBakedSpawnSlots (result.Slots);
        progress?.RecordSuccess (key, result.Slots);
        return result.FoundCount;
    }

    private static SpawnerBakeParams? CreateBakeParamsOrNull (MonsterSpawner spawner, bool fastCandidateGeneration)
    {
        var mobCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
        if (mobCount <= 0)
        {
            spawner.ClearSpawnError ();
            spawner.SetBakedSpawnSlots ([]);
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
            PoolCount = OutdoorFieldConfig.ComputeBakedSlotPoolCount (mobCount, spawnRadius),
            FastCandidateGeneration = fastCandidateGeneration,
        };
    }

    private static void TryFillPoolWithSpread (
        SpawnerBakeParams job,
        List<Vector3> validated,
        List<Vector3> picked,
        ref OutdoorSpawnSlotValidator.FailReason? lastFailure)
    {
        var samples = BuildSpreadSamplePool (job);
        if (job.UseShuffledCandidateFill)
        {
            ShuffleInPlace (samples);
            var maxAttempts = job.MaxCandidateAttempts > 0
                ? Math.Min (job.MaxCandidateAttempts, samples.Count)
                : samples.Count;
            for (var i = 0; i < maxAttempts && validated.Count < job.PoolCount; i++)
            {
                var (x, z) = samples[i];
                TryAddCandidate (job, x, z, validated, picked, ref lastFailure);
            }

            return;
        }

        var remainingAttempts = job.MaxCandidateAttempts > 0
            ? job.MaxCandidateAttempts
            : int.MaxValue;
        while (validated.Count < job.PoolCount && samples.Count > 0 && remainingAttempts-- > 0)
        {
            var bestIndex = FindFarthestSampleIndex (job.Origin, samples, picked);
            var (x, z) = samples[bestIndex];
            samples.RemoveAt (bestIndex);
            TryAddCandidate (job, x, z, validated, picked, ref lastFailure);
        }
    }

    private static void ShuffleInPlace (List<(float X, float Z)> samples)
    {
        for (var i = samples.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next (i + 1);
            (samples[i], samples[j]) = (samples[j], samples[i]);
        }
    }

    private static List<(float X, float Z)> BuildSpreadSamplePool (SpawnerBakeParams job)
    {
        var samples = new List<(float X, float Z)> ();
        var seen = new HashSet<(int, int)> ();
        var radiusSq = job.SpawnRadiusMeters * job.SpawnRadiusMeters;
        var spacing = job.CandidateSampleSpacingMeters > 0f
            ? job.CandidateSampleSpacingMeters
            : job.FastCandidateGeneration
                ? FastSampleSpacingMeters
                : OutdoorFieldConfig.MinSlotSeparationMeters;

        AddLooseGridSamples (
            job.Origin.X,
            job.Origin.Z,
            job.SpawnRadiusMeters,
            job.Origin,
            radiusSq,
            spacing,
            samples,
            seen);
        return samples;
    }

    /// <summary>
    /// Uniform XZ grid in a disc, no WalkData or atlas filtering
    /// </summary>
    private static void AddLooseGridSamples (
        float centerX,
        float centerZ,
        float collectRadius,
        Vector3 spawnerOrigin,
        float spawnRadiusSq,
        float sampleSpacingMeters,
        List<(float X, float Z)> samples,
        HashSet<(int, int)> seen)
    {
        var extent = Mathf.CeilToInt (collectRadius / sampleSpacingMeters);
        var collectRadiusSq = collectRadius * collectRadius;
        for (var z = -extent; z <= extent; z++)
        {
            for (var x = -extent; x <= extent; x++)
            {
                var worldX = centerX + x * sampleSpacingMeters;
                var worldZ = centerZ + z * sampleSpacingMeters;
                var cdx = worldX - centerX;
                var cdz = worldZ - centerZ;
                if (cdx * cdx + cdz * cdz > collectRadiusSq)
                {
                    continue;
                }

                AddSample (worldX, worldZ, spawnerOrigin, spawnRadiusSq, samples, seen);
            }
        }
    }

    private static void AddSample (
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

        var key = ((int) Mathf.Round (x * 4f), (int) Mathf.Round (z * 4f));
        if (!seen.Add (key))
        {
            return;
        }

        samples.Add ((x, z));
    }

    private static int FindFarthestSampleIndex (
        Vector3 origin,
        IReadOnlyList<(float X, float Z)> samples,
        IReadOnlyList<Vector3> picked)
    {
        var bestIndex = 0;
        var bestScore = float.MinValue;
        for (var i = 0; i < samples.Count; i++)
        {
            var (x, z) = samples[i];
            var score = MinDistanceScore (origin, picked, x, z);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static float MinDistanceScore (Vector3 origin, IReadOnlyList<Vector3> picked, float x, float z)
    {
        var minDistSq = DistanceSq (origin.X, origin.Z, x, z);
        foreach (var position in picked)
        {
            minDistSq = Math.Min (minDistSq, DistanceSq (position.X, position.Z, x, z));
        }

        return minDistSq;
    }

    private static float DistanceSq (float ax, float az, float bx, float bz)
    {
        var dx = ax - bx;
        var dz = az - bz;
        return dx * dx + dz * dz;
    }

    private static void TryAddCandidate (
        SpawnerBakeParams job,
        float x,
        float z,
        List<Vector3> validated,
        List<Vector3> picked,
        ref OutdoorSpawnSlotValidator.FailReason? lastFailure)
    {
        // Placeholder Y only. OutdoorSpawnSlotValidator ignores candidate.Y and probes at
        // spawnerOrigin.Y
        var candidate = new Vector3 (x, job.Origin.Y, z);
        var minSeparation = job.MinSlotSeparationMeters > 0f
            ? job.MinSlotSeparationMeters
            : OutdoorFieldConfig.MinSlotSeparationMeters;
        if (!IsSeparated (candidate, picked, minSeparation))
        {
            return;
        }

        if (!OutdoorSpawnSlotValidator.TryValidateCandidate (
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

        validated.Add (refinedCandidate);
        picked.Add (refinedCandidate);
    }

    private static void MarkBakeFailure (
        MonsterSpawner spawner,
        int mobCount,
        int poolCount,
        int foundCount,
        string detail)
    {
        // Headless bake-all prints region summaries only. Thousands of per-spawner lines stall the
        // console
        if (!MonsterSpawnSlotHeadlessBake.IsActive || _verboseHeadlessFailures)
        {
            GD.PushWarning (
                $"MonsterSpawnSlotBaker: spawner '{spawner.Name}' at {spawner.GlobalPosition}: "
                + $"found {foundCount}/{poolCount} pool slot(s), need {mobCount} mob(s) ({detail}).");
        }

        spawner.MarkSpawnError ();
        spawner.SetBakedSpawnSlots ([]);
    }

    private static bool IsSeparated (Vector3 candidate, IReadOnlyList<Vector3> picked, float minSeparationMeters)
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
