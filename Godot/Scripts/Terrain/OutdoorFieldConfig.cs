namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Shared tunables for outdoor walk, spawn-slot bake, and nav pathfinding.
/// </summary>
public static class OutdoorFieldConfig
{
    public const float SampleSpacingMeters = 0.25f;
    public const float MobBodyRadiusMeters = 0.4f;
    public const float MinSlotSeparationMeters = 0.7f;
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
}
