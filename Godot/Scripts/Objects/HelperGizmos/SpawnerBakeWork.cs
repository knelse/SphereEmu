using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

public readonly record struct SpawnerBakeParams
{
    public Vector3 Origin { get; init; }
    public float SpawnRadiusMeters { get; init; }
    public float LeashRadiusMeters { get; init; }
    public int MobCount { get; init; }
    public int PoolCount { get; init; }

    /// <summary>
    ///     Batch rebake: coarser XZ seeding and cheaper nav disc checks.
    /// </summary>
    public bool FastCandidateGeneration { get; init; }

    /// <summary>
    ///     Optional XZ seed grid spacing in meters. When &gt; 0, overrides the default
    ///     <see cref="FastCandidateGeneration" /> spacing (1.4 m fast / 0.7 m full).
    /// </summary>
    public float CandidateSampleSpacingMeters { get; init; }

    /// <summary>
    ///     When true, shuffle the seed pool and validate in order (O(n)) instead of
    ///     farthest-point picking (O(n²)). Used by alchemy material spawners.
    /// </summary>
    public bool UseShuffledCandidateFill { get; init; }

    /// <summary>
    ///     Hard cap on validation attempts. 0 = try the whole sample pool.
    /// </summary>
    public int MaxCandidateAttempts { get; init; }

    /// <summary>
    ///     Optional min separation between accepted slots. 0 = use
    ///     <see cref="OutdoorFieldConfig.MinSlotSeparationMeters" />.
    /// </summary>
    public float MinSlotSeparationMeters { get; init; }
}

public sealed class SpawnerBakeResult
{
    public bool Success { get; init; }
    public List<Vector3> Slots { get; init; } = [];
    public string? FailureDetail { get; init; }
    public int FoundCount { get; init; }
}
