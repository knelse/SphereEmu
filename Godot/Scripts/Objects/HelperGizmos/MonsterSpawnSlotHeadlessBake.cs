using System;
using System.Threading.Tasks;
using Godot;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Headless entry point for baking outdoor spawn slots on MainServer.
///     Run:
///     <c>godot --headless --path &lt;project&gt; res://Godot/Scenes/monster_spawn_slot_bake.tscn</c>
///     or <c>.\Tools\bake_spawn_slots.ps1</c>.
/// </summary>
public partial class MonsterSpawnSlotHeadlessBake : Node
{
    public const string DefaultScenePath = "res://Godot/Scenes/MainServer.tscn";
    public const string SphereServerScriptPath = "res://Server/SphereServer.cs";

    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    /// <summary>
    ///     Set while the bake scene owns MainServer so <see cref="MonsterSpawner" /> skips runtime spawn
    ///     activation in <c>_Ready</c>.
    /// </summary>
    public static bool IsActive { get; private set; }

    public override async void _Ready()
    {
        var options = ParseOptions();
        if (options.ShowHelp)
        {
            PrintHelp();
            Quit(ExitSuccess);
            return;
        }

        IsActive = true;
        try
        {
            var exit = await RunAsync(options);
            Quit(exit);
        }
        catch (Exception ex)
        {
            GD.PushError($"MonsterSpawnSlotHeadlessBake: failed: {ex}");
            Quit(ExitFailure);
        }
        finally
        {
            IsActive = false;
        }
    }

    private async Task<int> RunAsync(Options options)
    {
        if (!ResourceLoader.Exists(options.ScenePath))
        {
            GD.PushError($"MonsterSpawnSlotHeadlessBake: scene not found: {options.ScenePath}");
            return ExitFailure;
        }

        GD.Print($"MonsterSpawnSlotHeadlessBake: loading {options.ScenePath}…");
        var packed = ResourceLoader.Load<PackedScene>(options.ScenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
        if (packed is null)
        {
            GD.PushError("MonsterSpawnSlotHeadlessBake: failed to load PackedScene.");
            return ExitFailure;
        }

        // Keep the SphereServer script attached — clearing it disposes the C# wrapper and breaks
        // AddChild. SphereServer._Ready no-ops while IsActive is true.
        var root = packed.Instantiate<Node>();
        AddChild(root);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var spawners = root.GetNodeOrNull<Node>("MonsterSpawners");
        if (spawners is null)
        {
            GD.PushError("MonsterSpawnSlotHeadlessBake: MonsterSpawners node missing.");
            return ExitFailure;
        }

        var terrain = FindTerrainGridMap(root);
        if (terrain is null)
        {
            GD.PushError(
                "MonsterSpawnSlotHeadlessBake: Terrain GridMap not found under TerrainScene — "
                + "nav tile loading will fail.");
            return ExitFailure;
        }

        GD.Print(
            $"MonsterSpawnSlotHeadlessBake: terrain '{terrain.GetPath()}', "
            + $"{spawners.GetChildCount()} spawner node(s). Baking…");

        var settings = new SpawnSlotBakeAllSettings
        {
            YieldProcessFrames = false,
            ForceRebake = options.Force,
            ProgressFilePath = options.ProgressPath,
            NameContains = options.NameContains,
        };

        var slotCount = await MonsterSpawnSlotBaker.BakeAllUnderAsync(spawners, settings);

        if (!options.SkipSceneSave)
        {
            GD.Print("MonsterSpawnSlotHeadlessBake: packing and saving scene…");
            if (!TrySaveMainServer(root, options.ScenePath))
            {
                return ExitFailure;
            }
        }
        else
        {
            GD.Print(
                "MonsterSpawnSlotHeadlessBake: skipped scene save (--skip-scene-save); "
                + "results are in the progress sidecar only.");
        }

        GD.Print($"MonsterSpawnSlotHeadlessBake: done (baked slot count returned={slotCount}).");
        return ExitSuccess;
    }

    private static bool TrySaveMainServer(Node root, string scenePath)
    {
        try
        {
            var packed = new PackedScene();
            var packErr = packed.Pack(root);
            if (packErr != Error.Ok)
            {
                GD.PushError($"MonsterSpawnSlotHeadlessBake: Pack failed ({packErr}).");
                return false;
            }

            var saveErr = ResourceSaver.Save(packed, scenePath);
            if (saveErr != Error.Ok)
            {
                GD.PushError($"MonsterSpawnSlotHeadlessBake: ResourceSaver.Save failed ({saveErr}).");
                return false;
            }

            GD.Print($"MonsterSpawnSlotHeadlessBake: saved {scenePath}");
            return true;
        }
        catch (Exception ex)
        {
            GD.PushError($"MonsterSpawnSlotHeadlessBake: scene save threw ({ex.Message}).");
            return false;
        }
    }

    private static GridMap? FindTerrainGridMap(Node root)
    {
        foreach (var node in root.FindChildren("*", nameof(GridMap), recursive: true, owned: false))
        {
            if (node is GridMap grid && grid.Name == "Terrain")
            {
                return grid;
            }
        }

        return null;
    }

    private static Options ParseOptions()
    {
        var options = new Options();
        var args = OS.GetCmdlineUserArgs();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--force":
                case "-f":
                    options.Force = true;
                    break;
                case "--skip-scene-save":
                    options.SkipSceneSave = true;
                    break;
                case "--scene" when i + 1 < args.Length:
                    options.ScenePath = args[++i];
                    break;
                case "--progress" when i + 1 < args.Length:
                    options.ProgressPath = args[++i];
                    break;
                case "--name-contains" when i + 1 < args.Length:
                    options.NameContains = args[++i];
                    break;
            }
        }

        return options;
    }

    private static void PrintHelp()
    {
        GD.Print(
            """
            Monster spawn-slot headless bake

            Usage:
              godot --headless --path <project> res://Godot/Scenes/monster_spawn_slot_bake.tscn [-- options]
              .\Tools\bake_spawn_slots.ps1

            Options:
              --force             Ignore existing slots / progress sidecar; rebake all
              --skip-scene-save   Only write the progress sidecar (no MainServer.tscn pack)
              --scene <path>      Scene containing MonsterSpawners + Terrain (default MainServer)
              --progress <path>   Sidecar JSON path (default res://Godot/Terrain/spawn_slot_bake_progress.json)
              --name-contains <s> Only bake spawners whose name/key contains s (debug)
              --help              Show this help

            Checkpoints every 100 dirty spawners go to the sidecar (fast). The scene is packed once at the end.
            Re-run the same command after an interrupt to resume from the sidecar.
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

    private sealed class Options
    {
        public bool ShowHelp { get; set; }
        public bool Force { get; set; }
        public bool SkipSceneSave { get; set; }
        public string ScenePath { get; set; } = DefaultScenePath;
        public string ProgressPath { get; set; } = SpawnSlotBakeProgress.DefaultResourcePath;
        public string? NameContains { get; set; }
    }
}
