namespace SphServer.Godot.Scripts.Terrain;

public static class OutdoorFieldConfig
{
    public const float SampleSpacingMeters = 0.25f;
    public const float MobBodyRadiusMeters = 0.4f;
    public const float MinSlotSeparationMeters = 0.7f;
    public const int MinBakedSpawnSlotsPerSpawner = 10;
    public const float OpennessRadiusMeters = 2f;
    public const float DefaultSpawnRadiusMeters = 7f;
    public const float DefaultLeashRadiusMeters = 100f;
    public const float MaxOutdoorSpawnAboveTerrainMeters = 1.25f;

    /// <summary>
    /// Bake may drop a floating outdoor center straight down, at most this far, and measure spawn
    /// radius from the nav hit
    /// </summary>
    public const float MaxOutdoorDropToNavMeshMeters = 50f;

    /// <summary>
    /// Wrong-level snaps sit much farther in Y than real terrain, including cliffs
    /// </summary>
    public const float MaxSpawnSlotVerticalDriftRadiusMultiplier = 1.5f;

    public const float MinSpawnSlotVerticalDriftMeters = 15f;
    public const int BlockedDilationRadiusCells = 1;
    public const int AStarMaxExpandedNodes = 10_000;
    public const int PathRequestsPerTick = 48;

    /// <summary>
    /// False keeps monsters off nav paths
    /// </summary>
    public static readonly bool NavigationMobMovementEnabled = false;

    public static int ComputeBakedSlotPoolCount (int mobCount, float spawnRadiusMeters)
    {
        var desired = Math.Max (mobCount, MinBakedSpawnSlotsPerSpawner);
        var maxInRadius = EstimateMaxSeparatedSlots (spawnRadiusMeters, MinSlotSeparationMeters);
        return Math.Min (desired, maxInRadius);
    }

    private static int EstimateMaxSeparatedSlots (float radiusMeters, float minSeparationMeters)
    {
        if (radiusMeters <= minSeparationMeters * 0.5f)
        {
            return 1;
        }

        var area = Math.PI * radiusMeters * radiusMeters;
        var cellArea = minSeparationMeters * minSeparationMeters;
        return Math.Max (1, (int) Math.Floor (area / cellArea));
    }
}
