using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Navigation;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Headless bake of alchemy material spawn slots into MainServer.
///     Run: <c>.\Tools\bake_alchemy_spawn_slots.ps1</c>
/// </summary>
public partial class AlchemyMaterialSpawnSlotHeadlessBake : Node
{
    public const string DefaultScenePath = "res://Godot/Scenes/MainServer.tscn";

    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    /// <summary>
    ///     Set while this bake owns MainServer so alchemy / server runtime paths skip activation.
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
            Quit(await RunAsync(options));
        }
        catch (Exception ex)
        {
            GD.PushError($"AlchemyMaterialSpawnSlotHeadlessBake: failed: {ex}");
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
            GD.PushError($"AlchemyMaterialSpawnSlotHeadlessBake: scene not found: {options.ScenePath}");
            return ExitFailure;
        }

        GD.Print($"AlchemyMaterialSpawnSlotHeadlessBake: loading {options.ScenePath}…");
        var packed = ResourceLoader.Load<PackedScene>(options.ScenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
        if (packed is null)
        {
            GD.PushError("AlchemyMaterialSpawnSlotHeadlessBake: failed to load PackedScene.");
            return ExitFailure;
        }

        var root = packed.Instantiate<Node>();
        AddChild(root);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var spawnersRoot = root.GetNodeOrNull<Node>("AlchemyMaterialSpawners");
        if (spawnersRoot is null)
        {
            GD.PushError("AlchemyMaterialSpawnSlotHeadlessBake: AlchemyMaterialSpawners node missing.");
            return ExitFailure;
        }

        var terrain = FindTerrainGridMap(root);
        if (terrain is null)
        {
            GD.PushError(
                "AlchemyMaterialSpawnSlotHeadlessBake: Terrain GridMap not found under TerrainScene — "
                + "nav tile loading will fail.");
            return ExitFailure;
        }

        var bakedOk = 0;
        var bakedFail = 0;
        var skipped = 0;
        var totalSlots = 0;
        var sw = Stopwatch.StartNew();

        GD.Print(
            $"AlchemyMaterialSpawnSlotHeadlessBake: terrain '{terrain.GetPath()}', "
            + $"{spawnersRoot.GetChildCount()} spawner node(s). Baking…");

        // Per-spawner tile load: registering the whole world into one nav map makes closest-point
        // snap highland markers onto canyon mesh (WrongLevel). Keep loads local; bake path is the
        // same shuffled BakeFast fill as the inspector button.
        foreach (var child in spawnersRoot.GetChildren())
        {
            if (child is not AlchemyMaterialSpawner spawner)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(options.NameContains)
                && !spawner.Name.ToString().Contains(options.NameContains, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var hasSlots = spawner.BakedSpawnSlots.Count > 0 && !spawner.HasBakeError;
            if (!options.Force && hasSlots)
            {
                skipped++;
                continue;
            }

            // Keep only this spawner's neighborhood on the nav map (avoids WrongLevel snaps).
            TerrainNavMeshRuntime.UnloadAllRegions();
            var count = await AlchemyMaterialSpawnSlotBaker.BakeForSpawnerAsync(spawner);
            if (count > 0 && !spawner.HasBakeError)
            {
                bakedOk++;
                totalSlots += count;
            }
            else
            {
                bakedFail++;
                GD.PushWarning(
                    $"AlchemyMaterialSpawnSlotHeadlessBake: '{spawner.Name}' failed "
                    + $"({spawner.BakeErrorDetail})");
            }
        }

        GD.Print(
            $"AlchemyMaterialSpawnSlotHeadlessBake: ok={bakedOk} fail={bakedFail} skipped={skipped} "
            + $"slots={totalSlots} in {sw.Elapsed.TotalSeconds:0.0}s");

        if (!options.SkipSceneSave)
        {
            GD.Print("AlchemyMaterialSpawnSlotHeadlessBake: packing and saving scene…");
            if (!TrySaveMainServer(root, options.ScenePath))
            {
                return ExitFailure;
            }
        }
        else
        {
            GD.Print("AlchemyMaterialSpawnSlotHeadlessBake: skipped scene save (--skip-scene-save).");
        }

        return bakedFail > 0 && bakedOk == 0 ? ExitFailure : ExitSuccess;
    }

    private static bool TrySaveMainServer(Node root, string scenePath)
    {
        try
        {
            var packed = new PackedScene();
            var packErr = packed.Pack(root);
            if (packErr != Error.Ok)
            {
                GD.PushError($"AlchemyMaterialSpawnSlotHeadlessBake: Pack failed ({packErr}).");
                return false;
            }

            var saveErr = ResourceSaver.Save(packed, scenePath);
            if (saveErr != Error.Ok)
            {
                GD.PushError($"AlchemyMaterialSpawnSlotHeadlessBake: ResourceSaver.Save failed ({saveErr}).");
                return false;
            }

            GD.Print($"AlchemyMaterialSpawnSlotHeadlessBake: saved {scenePath}");
            return true;
        }
        catch (Exception ex)
        {
            GD.PushError($"AlchemyMaterialSpawnSlotHeadlessBake: scene save threw ({ex.Message}).");
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
            Alchemy material spawn-slot headless bake

            Usage:
              .\Tools\bake_alchemy_spawn_slots.ps1

            Options:
              --force             Rebake spawners that already have slots
              --skip-scene-save   Do not pack MainServer.tscn
              --scene <path>      Scene containing AlchemyMaterialSpawners + Terrain
              --name-contains <s> Only bake spawners whose name contains s
              --help              Show this help

            By default only spawners with no slots (or bake error) are baked.
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
        public string? NameContains { get; set; }
    }
}
