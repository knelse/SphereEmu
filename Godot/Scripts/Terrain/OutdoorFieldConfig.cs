namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Shared tunables for outdoor walk, spawn-slot bake, and nav pathfinding.
/// </summary>
public static class OutdoorFieldConfig
{
    public const float SampleSpacingMeters = 0.25f;
    public const float MobBodyRadiusMeters = 0.4f;
    public const float MinSlotSeparationMeters = 0.7f;
    public const int MinBakedSpawnSlotsPerSpawner = 10;
    public const float OpennessRadiusMeters = 2f;
    public const float OpennessThreshold = 0.65f;
    public const float OverheadRayHeightMeters = 4f;
    public const float OverheadMinClearanceMeters = 2.5f;
    public const float DefaultSpawnRadiusMeters = 20f;
    public const float DefaultLeashRadiusMeters = 100f;
    public const int BlockedDilationRadiusCells = 1;
    public const int AStarMaxExpandedNodes = 10_000;
    public const int PathRequestsPerTick = 48;
    public const float PathReplanIntervalSeconds = 0.45f;

    public static int ComputeBakedSlotPoolCount(int mobCount, float spawnRadiusMeters)
    {
        var desired = Math.Max(mobCount, MinBakedSpawnSlotsPerSpawner);
        var maxInRadius = EstimateMaxSeparatedSlots(spawnRadiusMeters, MinSlotSeparationMeters);
        return Math.Min(desired, maxInRadius);
    }

    public static float ResolveOpennessRadiusMeters(float spawnRadiusMeters)
    {
        var radius = Math.Min(OpennessRadiusMeters, spawnRadiusMeters);
        return Math.Max(radius, SampleSpacingMeters);
    }

    private static int EstimateMaxSeparatedSlots(float radiusMeters, float minSeparationMeters)
    {
        if (radiusMeters <= minSeparationMeters * 0.5f)
        {
            return 1;
        }

        var area = Math.PI * radiusMeters * radiusMeters;
        var cellArea = minSeparationMeters * minSeparationMeters;
        return Math.Max(1, (int)Math.Floor(area / cellArea));
    }
}
