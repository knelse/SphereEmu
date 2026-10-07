using System;
using System.Diagnostics;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

public sealed class SpawnSlotBakeAllSettings
{
    /// <summary>
    /// Editor default awaits one process frame so the UI can paint. Headless leaves this false
    /// </summary>
    public bool YieldProcessFrames { get; init; } = true;

    /// <summary>
    /// Ignores existing slots and progress
    /// </summary>
    public bool ForceRebake { get; init; }

    /// <summary>
    /// Written every 100 dirty spawners. The next run reloads it, so an interrupted headless bake
    /// resumes
    /// </summary>
    public string? ProgressFilePath { get; init; }

    /// <summary>
    /// Editor bake saves the scene here. Headless leaves this null and uses the progress sidecar
    /// </summary>
    public Action<int, int, Stopwatch>? OnCheckpoint { get; init; }

    /// <summary>
    /// Only spawners whose key or name contains this (debug / one spawner)
    /// </summary>
    public string? NameContains { get; init; }
}
