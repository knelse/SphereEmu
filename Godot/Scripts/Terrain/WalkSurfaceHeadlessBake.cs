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
            if (options.ConvertChunksOnly)
            {
                GD.Print("WalkSurfaceHeadlessBake: converting walk-surface chunks to format v4 (outdoor spawn channel)...");
                var conversion = WalkSurfaceChunkConverter.ConvertDirectory(
                    gridFill.WalkSurfaceDataDirectory,
                    skipAlreadyCurrent: !options.Force);
                if (conversion.Converted == 0 && conversion.Failed == 0)
                {
                    GD.Print("WalkSurfaceHeadlessBake: all chunks already format v4 (use --force to rebuild spawn channel).");
                }

                return conversion.Succeeded ? ExitSuccess : ExitFailure;
            }

            if (options.ObjectsOnly)
            {
                GD.Print("WalkSurfaceHeadlessBake: re-stamping terrain object footprints and heights only.");
                var restampedChunks = gridFill.RestampWalkSurfaceObjectFootprints();
                if (restampedChunks <= 0)
                {
                    GD.PushError("WalkSurfaceHeadlessBake: object re-stamp produced no walk-surface chunks.");
                    return ExitFailure;
                }

                GD.Print($"WalkSurfaceHeadlessBake: done ({restampedChunks} chunk(s) written).");
                return ExitSuccess;
            }

            GD.Print("WalkSurfaceHeadlessBake: rebuilding terrain grid from map...");
            if (!gridFill.RebuildTerrainGrid())
            {
                return ExitFailure;
            }

            GD.Print("WalkSurfaceHeadlessBake: baking walk surface (terrain heights + object walk data)...");
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
                case "--convert-chunks":
                    options.ConvertChunksOnly = true;
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
              --objects-only    Re-stamp terrain object blocked footprints and heights onto existing chunks
              --convert-chunks  Convert existing chunk_*.bin files to compressed format v3 (no rebake)
              --help            Show this help

            Default run rebuilds the terrain GridMap from map.txt, bakes height samples at 0.25m spacing, then stamps
            blocked footprints for all terrain objects (plants, rocks, buildings) and height templates for buildings/town geometry.
            Use --convert-chunks to shrink legacy v1/v2 chunk files on disk without recomputing heights.
            Add --force with --convert-chunks to recompress files that are already v3.
            Use --force after changing sample spacing, chunk compression format, or when replacing old 2m walk chunks.
            Chunks are saved as format v3 (quantized heights, bit-packed blocked flags, Deflate).
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
        public bool ConvertChunksOnly { get; set; }
    }
}
