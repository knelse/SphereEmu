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
    /// Batch rebake: coarser XZ seeding and a cheaper nav disc
    /// </summary>
    public bool FastCandidateGeneration { get; init; }

    /// <summary>
    /// Above 0, overrides FastCandidateGeneration spacing (1.4 m fast, 0.7 m full)
    /// </summary>
    public float CandidateSampleSpacingMeters { get; init; }

    /// <summary>
    /// Shuffle then validate in order, O(n), instead of farthest-point O(n^2). Alchemy material
    /// spawners use this
    /// </summary>
    public bool UseShuffledCandidateFill { get; init; }

    /// <summary>
    /// 0 tries the whole sample pool
    /// </summary>
    public int MaxCandidateAttempts { get; init; }

    /// <summary>
    /// 0 uses OutdoorFieldConfig.MinSlotSeparationMeters
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
