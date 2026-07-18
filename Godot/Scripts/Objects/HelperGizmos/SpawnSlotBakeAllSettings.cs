using System;
using System.Diagnostics;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>Options for <see cref="MonsterSpawnSlotBaker.BakeAllUnderAsync" />.</summary>
public sealed class SpawnSlotBakeAllSettings
{
    /// <summary>
    ///     When true (editor default), await one process frame between region sync and bake so the UI can
    ///     paint. Headless should leave this false.
    /// </summary>
    public bool YieldProcessFrames { get; init; } = true;

    /// <summary>
    ///     When true, ignore existing baked slots / progress and rebake every dirty-eligible spawner.
    /// </summary>
    public bool ForceRebake { get; init; }

    /// <summary>
    ///     Optional sidecar JSON (<see cref="SpawnSlotBakeProgress.DefaultResourcePath" />). Written every
    ///     100 dirty spawners; reloaded on the next run so interrupted headless bakes resume.
    /// </summary>
    public string? ProgressFilePath { get; init; }

    /// <summary>
    ///     Optional hook after each checkpoint. Editor bake wires this to SaveScene; headless leaves it
    ///     null and relies on the progress sidecar (+ optional final scene pack).
    /// </summary>
    public Action<int, int, Stopwatch>? OnCheckpoint { get; init; }
}
