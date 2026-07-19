using System;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Navigation;
using SphServer.Godot.Scripts.Terrain;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Bakes navmesh spawn slots for <see cref="AlchemyMaterialSpawner" />.
///     Fast path only: coarse seeds, shuffled O(n) fill, capped attempts, BakeFast discs.
/// </summary>
public static class AlchemyMaterialSpawnSlotBaker
{
    public const int DefaultSlotPoolCount = 20;
    private const int MinSlotPoolCount = 1;
    private const int MaxSlotPoolCount = 256;

    private const float MinSampleSpacingMeters = 2f;
    private const float MaxSampleSpacingMeters = 12f;
    private const int TargetSamplesPerSlot = 12;
    private const int MaxAttemptsPerSlot = 24;

    public static int BakeForSpawner(AlchemyMaterialSpawner spawner)
    {
        var job = CreateJob(spawner, out var poolCount, out var minSuccess);
        TerrainNavMeshRuntime.EnsureTilesLoaded(spawner, job.Origin, job.SpawnRadiusMeters + 1f);
        TerrainNavMeshRuntime.TrySyncImmediate();
        return Apply(spawner, MonsterSpawnSlotBaker.BakeCore(job), poolCount, minSuccess);
    }

    public static async Task<int> BakeForSpawnerAsync(AlchemyMaterialSpawner spawner)
    {
        var job = CreateJob(spawner, out var poolCount, out var minSuccess);
        var newlyLoaded = TerrainNavMeshRuntime.EnsureTilesLoaded(
            spawner, job.Origin, job.SpawnRadiusMeters + 1f);

        var tree = spawner.GetTree();
        if (newlyLoaded && tree is not null)
        {
            await TerrainNavMeshRuntime.SyncAsync(tree);
        }
        else
        {
            TerrainNavMeshRuntime.TrySyncImmediate();
        }

        var result = MonsterSpawnSlotBaker.BakeCore(job);
        if (!result.Success && newlyLoaded && tree is not null)
        {
            await TerrainNavMeshRuntime.SyncAsync(tree, force: true);
            result = MonsterSpawnSlotBaker.BakeCore(job);
        }

        return Apply(spawner, result, poolCount, minSuccess);
    }

    private static SpawnerBakeParams CreateJob(
        AlchemyMaterialSpawner spawner,
        out int poolCount,
        out int minSuccess)
    {
        var radius = Mathf.Max(0.5f, spawner.SpawnRadiusMeters);
        poolCount = Mathf.Clamp(spawner.SpawnSlotCount, MinSlotPoolCount, MaxSlotPoolCount);
        minSuccess = Mathf.Min(poolCount, Mathf.Max(1, spawner.MaxCount));
        // Mild separation so shuffled fill still spreads without starving sparse areas.
        var minSeparation = Mathf.Max(
            OutdoorFieldConfig.MinSlotSeparationMeters,
            radius / Mathf.Max(1f, Mathf.Sqrt(poolCount) * 3.5f));
        return new SpawnerBakeParams
        {
            Origin = spawner.GlobalPosition,
            SpawnRadiusMeters = radius,
            LeashRadiusMeters = radius,
            MobCount = minSuccess,
            PoolCount = poolCount,
            FastCandidateGeneration = true,
            CandidateSampleSpacingMeters = ComputeSampleSpacing(radius, poolCount),
            UseShuffledCandidateFill = true,
            MaxCandidateAttempts = poolCount * MaxAttemptsPerSlot,
            MinSlotSeparationMeters = minSeparation,
        };
    }

    internal static float ComputeSampleSpacing(float radiusMeters, int poolCount)
    {
        var targetSamples = Math.Max(poolCount * TargetSamplesPerSlot, 48);
        var spacing = radiusMeters * MathF.Sqrt(MathF.PI / targetSamples);
        return Mathf.Clamp(spacing, MinSampleSpacingMeters, MaxSampleSpacingMeters);
    }

    private static int Apply(
        AlchemyMaterialSpawner spawner,
        SpawnerBakeResult result,
        int poolCount,
        int minSuccess)
    {
        if (!result.Success || result.Slots.Count < minSuccess)
        {
            spawner.MarkBakeError(result.FailureDetail ?? "insufficient walkable candidates");
            GD.PushWarning(
                $"AlchemyMaterialSpawnSlotBaker: '{spawner.Name}' bake failed "
                + $"({result.FoundCount}/{poolCount}): {result.FailureDetail}");
            return 0;
        }

        spawner.SetBakedSpawnSlots(result.Slots);
        GD.Print(
            $"AlchemyMaterialSpawnSlotBaker: '{spawner.Name}' baked {result.Slots.Count} slot(s) "
            + $"(requested {poolCount}, radius {spawner.SpawnRadiusMeters:0.##}m).");
        return result.Slots.Count;
    }
}
