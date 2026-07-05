using System;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
///     Headless entry point for rebaking outdoor walk-surface atlases from a physics world
///     (terrain GridMap collision + StaticBody colliders for plants, rocks, buildings, and props).
///     Run: godot --headless --path &lt;project&gt; res://Godot/Scenes/physics_standing_surface_bake.tscn
/// </summary>
public partial class PhysicsStandingSurfaceHeadlessBake : Node
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int PhysicsSettleFrames = 3;

    private int _physicsFramesWaited;
    private bool _bakeStarted;

    public override void _Ready()
    {
        var options = ParseOptions();
        if (options.ShowHelp)
        {
            PrintHelp();
            Quit(ExitSuccess);
            return;
        }

        _bakeStarted = true;
        GD.Print("PhysicsStandingSurfaceHeadlessBake: waiting for physics to settle...");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_bakeStarted || _physicsFramesWaited >= PhysicsSettleFrames)
        {
            return;
        }

        _physicsFramesWaited++;
        if (_physicsFramesWaited < PhysicsSettleFrames)
        {
            return;
        }

        _bakeStarted = false;
        var gridFill = GetNode<TerrainGridFill>("TerrainGridFill");
        var colliderFill = GetNode<TerrainPhysicsColliderFill>("TerrainPhysicsColliderFill");
        var exitCode = RunBake(gridFill, colliderFill);
        Quit(exitCode);
    }

    private static int RunBake(TerrainGridFill gridFill, TerrainPhysicsColliderFill colliderFill)
    {
        try
        {
            GD.Print("PhysicsStandingSurfaceHeadlessBake: rebuilding terrain grid and physics colliders...");
            GD.Print($"  object JSON: {colliderFill.ObjectDataDirectory}");
            GD.Print($"  models:      {colliderFill.ModelsDirectory}");
            if (!colliderFill.RebuildPhysicsColliders())
            {
                GD.PushError("PhysicsStandingSurfaceHeadlessBake: physics collider build failed.");
                return ExitFailure;
            }

            var terrain = colliderFill.ResolveTerrainGridMap();
            if (terrain is null)
            {
                GD.PushError("PhysicsStandingSurfaceHeadlessBake: terrain GridMap missing after rebuild.");
                return ExitFailure;
            }

            var stats = colliderFill.LastBuildStats;
            if (stats is not null)
            {
                GD.Print(
                    $"PhysicsStandingSurfaceHeadlessBake: colliders ready — "
                    + $"terrain cells={stats.TerrainCells}, object bodies={stats.ObjectBodies} "
                    + $"(plants={stats.Plants}, rocks={stats.Rocks}, buildings/props={stats.BuildingsAndProps}).");
            }

            GD.Print("PhysicsStandingSurfaceHeadlessBake: sampling physics into walk atlas...");
            var savedChunks = PhysicsStandingSurfaceAtlasBuilder.BuildAndRebakeNav(
                colliderFill,
                terrain,
                gridFill,
                clearExistingChunks: true);
            if (savedChunks <= 0)
            {
                GD.PushError("PhysicsStandingSurfaceHeadlessBake: bake produced no walk-surface chunks.");
                return ExitFailure;
            }

            GD.Print($"PhysicsStandingSurfaceHeadlessBake: done ({savedChunks} chunk(s) written + nav rebaked).");
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            GD.PushError($"PhysicsStandingSurfaceHeadlessBake: failed: {ex}");
            return ExitFailure;
        }
    }

    private static HeadlessBakeOptions ParseOptions()
    {
        var options = new HeadlessBakeOptions();
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            switch (arg)
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--bake-standing-surface":
                    options.BakeStandingSurface = true;
                    break;
            }
        }

        return options;
    }

    private static void PrintHelp()
    {
        GD.Print(
            """
            Physics standing-surface headless bake

            Usage:
              godot --headless --path <project> res://Godot/Scenes/physics_standing_surface_bake.tscn [-- options]

            Options:
              --bake-standing-surface  Accepted alias (default run always bakes)
              --help                   Show this help

            Default run rebuilds the terrain GridMap, adds physics colliders for terrain tiles and every
            terrain object placement (plants, trees, rocks, buildings, props), samples standing surfaces
            into walk atlas chunks at 0.25 m, and rebakes outdoor nav.
            """);
    }

    private void Quit(int exitCode)
    {
        CallDeferred(nameof(QuitTree), exitCode);
    }

    private void QuitTree(int exitCode)
    {
        GetTree().Quit(exitCode);
    }

    private sealed class HeadlessBakeOptions
    {
        public bool ShowHelp { get; set; }
        public bool BakeStandingSurface { get; set; } = true;
    }
}
