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
    public const float DefaultSpawnRadiusMeters = 7f;
    public const float DefaultLeashRadiusMeters = 100f;
    public const float MaxOutdoorSpawnAboveTerrainMeters = 1.25f;

    /// <summary>
    ///     When an outdoor spawner floats above the navmesh, bake may drop the spawn center straight down
    ///     by at most this many meters to the first nav intersection and measure the spawn radius from there.
    /// </summary>
    public const float MaxOutdoorDropToNavMeshMeters = 50f;

    /// <summary>
    ///     Multiplier applied to a spawner's spawn radius to get its max plausible height difference between
    ///     the spawner's own placement Y and a validated slot's navmesh-snapped Y (floor'd by
    ///     <see cref="MinSpawnSlotVerticalDriftMeters" />). <see cref="Navigation.TerrainNavMeshRuntime" />'s
    ///     disc check only cares about horizontal (XZ) navmesh containment (see its docs for why), so near
    ///     multi-level geometry (towers, dungeons, bridges) it can snap onto a polygon on a completely
    ///     different level that happens to sit above/below the query point in XZ. Real outdoor terrain (steep
    ///     mountain/plateau cliffs included) can plausibly vary by more than the spawn radius itself within
    ///     that radius, but a different building floor/roof sits an order of magnitude further away
    ///     vertically - empirically ~25m for a legitimate cliff-edge case vs. 150m+ for an actual wrong-level
    ///     snap, on a 20m spawn radius - so scaling with the radius (rather than a flat constant) keeps this
    ///     tolerant of terrain shape while still rejecting the multi-level case with a comfortable margin.
    /// </summary>
    public const float MaxSpawnSlotVerticalDriftRadiusMultiplier = 1.5f;

    public const float MinSpawnSlotVerticalDriftMeters = 15f;
    public const int BlockedDilationRadiusCells = 1;
    public const int AStarMaxExpandedNodes = 10_000;
    public const int PathRequestsPerTick = 48;

    public static int ComputeBakedSlotPoolCount(int mobCount, float spawnRadiusMeters)
    {
        var desired = Math.Max(mobCount, MinBakedSpawnSlotsPerSpawner);
        var maxInRadius = EstimateMaxSeparatedSlots(spawnRadiusMeters, MinSlotSeparationMeters);
        return Math.Min(desired, maxInRadius);
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
