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
    ///     Batch rebake: coarser XZ seeding, no atlas dual-collect, cheaper nav disc checks.
    /// </summary>
    public bool FastCandidateGeneration { get; init; }
}

public sealed class SpawnerBakeResult
{
    public bool Success { get; init; }
    public List<Vector3> Slots { get; init; } = [];
    public string? FailureDetail { get; init; }
    public int FoundCount { get; init; }
}
