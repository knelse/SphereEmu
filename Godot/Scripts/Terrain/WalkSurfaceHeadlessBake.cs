using System;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Headless entry point for rebaking outdoor walk-surface atlases (terrain heights + object footprints).
///     Run: godot --headless --path &lt;project&gt; res://Godot/Scenes/walk_surface_bake.tscn [-- --force] [-- --objects-only]
/// </summary>
public partial class WalkSurfaceHeadlessBake : Node
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    public override void _Ready()
    {
        var options = ParseOptions();
        if (options.ShowHelp)
        {
            PrintHelp();
            Quit(ExitSuccess);
            return;
        }

        var gridFill = GetNode<TerrainGridFill>("TerrainGridFill");
        var exitCode = RunBake(gridFill, options);
        Quit(exitCode);
    }

    private static int RunBake(TerrainGridFill gridFill, HeadlessBakeOptions options)
    {
        try
        {
            if (options.ObjectsOnly)
            {
                GD.Print("WalkSurfaceHeadlessBake: re-stamping terrain object footprints only.");
                return gridFill.RestampWalkSurfaceObjectFootprints() > 0 ? ExitSuccess : ExitFailure;
            }

            GD.Print("WalkSurfaceHeadlessBake: rebuilding terrain grid from map...");
            if (!gridFill.RebuildTerrainGrid())
            {
                return ExitFailure;
            }

            GD.Print("WalkSurfaceHeadlessBake: baking walk surface (terrain heights + object footprints)...");
            GD.Print($"  object JSON: {gridFill.WalkSurfaceObjectDataDirectory}");
            GD.Print($"  models:      {gridFill.WalkSurfaceModelsDirectory}");
            var savedChunks = gridFill.BakeWalkSurfaceAtlas(
                forceFullRebuild: options.Force,
                resumeFromProgress: !options.Force);
            if (savedChunks <= 0)
            {
                GD.PushError("WalkSurfaceHeadlessBake: bake produced no walk-surface chunks.");
                return ExitFailure;
            }

            GD.Print($"WalkSurfaceHeadlessBake: done ({savedChunks} chunk(s) written).");
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            GD.PushError($"WalkSurfaceHeadlessBake: failed: {ex}");
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
                case "--force":
                case "-f":
                    options.Force = true;
                    break;
                case "--objects-only":
                    options.ObjectsOnly = true;
                    break;
            }
        }

        return options;
    }

    private static void PrintHelp()
    {
        GD.Print(
            """
            Walk surface headless bake

            Usage:
              godot --headless --path <project> res://Godot/Scenes/walk_surface_bake.tscn [-- options]

            Options:
              --force           Clear partial progress and rebake all terrain heights from scratch
              --objects-only    Re-stamp terrain object blocked footprints onto existing chunks
              --help            Show this help

            Default run rebuilds the terrain GridMap from map.txt, bakes height samples, then stamps
            blocked footprints from Godot/Terrain/ObjectDataJson/ (plants, rocks, other, extra folders).
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
        public bool Force { get; set; }
        public bool ObjectsOnly { get; set; }
    }
}
