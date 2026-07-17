using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using SphServer.Godot.Scripts.Navigation;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
///     Editor tool: writes production <see cref="NavigationMesh" /> <c>.res</c> files under
///     <see cref="NavMeshResourcesDirectory" /> by shelling out to
///     <c>Tools/bake_bulk_nav_glbs.ps1</c> → <c>Tools/bake_and_export_single_nav.gd</c>.
///     That GDScript path owns castle/town carve, arch portals, welds, and prune — do not reimplement
///     Recast source-geometry carving here. Checkpoint params/overrides come from
///     <see cref="CheckpointJsonPath" /> (<c>explicit_env</c> + per-group <c>env_overrides</c>).
/// </summary>
[Tool]
public partial class TerrainNavigationBaker : Node3D
{
    public const string NavigationRegionsRootName = "TerrainNavigation";

    /// <summary>Directory where baked per-tile NavigationMesh .res files are written (by the bake script).</summary>
    [Export]
    public string NavMeshResourcesDirectory { get; set; } = "res://Godot/Terrain/GeneratedNavMeshes/";

    /// <summary>
    ///     Repo-relative checkpoint JSON (default: CC checkpoint with <c>explicit_env</c> baseline +
    ///     tuned group overrides such as undercroft / archway profiles).
    /// </summary>
    [Export]
    public string CheckpointJsonPath { get; set; } = "Tools/nav_bake_checkpoint_cc.json";

    /// <summary>
    ///     Plan filter passed to <c>plan_bulk_nav_bakes.py</c> (<c>all</c>, <c>cc</c>, <c>town</c>, …).
    ///     Ignored when <see cref="BakeOnlyTileGroupKey" /> is set.
    /// </summary>
    [Export]
    public string BakeFilter { get; set; } = "all";

    /// <summary>
    ///     Concurrent Godot bake workers (<c>-Jobs</c>). 0 = processor count.
    /// </summary>
    [Export]
    public int MaxConcurrentBakeJobs { get; set; }

    /// <summary>
    ///     When non-empty, bake only the plan group that owns this tile/group key
    ///     (e.g. <c>Town4_00_00</c> or <c>Town4_occ00</c> — towns/CC 2×2 expand to four tiles).
    /// </summary>
    [Export]
    public string BakeOnlyTileGroupKey { get; set; } = "";

    /// <summary>
    ///     Log/manifest directory for the orchestrator (not GLB output). Empty →
    ///     <c>Tools/_nav_bake_project_out</c> under the project root.
    /// </summary>
    [Export]
    public string PreviewOutDirectory { get; set; } = "";

    [ExportToolButton("Bake terrain navigation")]
    public Callable BakeTerrainNavigationButton => Callable.From(() => BakeTerrainNavigation());

    /// <summary>
    ///     Runs the checkpointed headless bake (<c>--bake-only</c> → <see cref="NavMeshResourcesDirectory" />
    ///     <c>.res</c> files; no preview GLBs). Returns OK group count from the orchestrator.
    /// </summary>
    public int BakeTerrainNavigation()
    {
        if (PersistRegionsInScene)
        {
            GD.PushWarning(
                "TerrainNavigationBaker: PersistRegionsInScene is ignored — the bake script writes .res files only. "
                + "Use TerrainNavMeshRuntime to load them.");
        }

        var repoRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        var ps1 = Path.Combine(repoRoot, "Tools", "bake_bulk_nav_glbs.ps1");
        if (!File.Exists(ps1))
        {
            GD.PushError($"TerrainNavigationBaker: missing orchestrator script: {ps1}");
            return 0;
        }

        var checkpointRel = (CheckpointJsonPath ?? "").Replace('\\', '/').TrimStart('/');
        var checkpointAbs = Path.GetFullPath(Path.Combine(repoRoot, checkpointRel));
        if (!File.Exists(checkpointAbs))
        {
            GD.PushError($"TerrainNavigationBaker: checkpoint not found: {checkpointAbs}");
            return 0;
        }

        var outDir = string.IsNullOrWhiteSpace(PreviewOutDirectory)
            ? Path.Combine(repoRoot, "Tools", "_nav_bake_project_out")
            : PreviewOutDirectory.StartsWith("res://", StringComparison.Ordinal)
                ? ProjectSettings.GlobalizePath(PreviewOutDirectory)
                : PreviewOutDirectory;
        Directory.CreateDirectory(outDir);

        var jobs = MaxConcurrentBakeJobs > 0
            ? MaxConcurrentBakeJobs
            : global::System.Environment.ProcessorCount;

        var args = new StringBuilder();
        args.Append("-NoProfile -ExecutionPolicy Bypass -File ");
        args.Append(QuotePowerShell(ps1));
        args.Append(" -Out ").Append(QuotePowerShell(outDir));
        args.Append(" -CheckpointJson ").Append(QuotePowerShell(checkpointAbs));
        args.Append(" -WriteRes"); // .res only (orchestrator skips GLB unless -ExportGlb)
        args.Append(" -Jobs ").Append(jobs);

        var bakeOnly = !string.IsNullOrWhiteSpace(BakeOnlyTileGroupKey);
        if (bakeOnly)
        {
            // Still plan from map so -Tile can expand 1x1 keys into 2x2 town/CC groups.
            args.Append(" -Filter all");
            args.Append(" -Tile ").Append(QuotePowerShell(BakeOnlyTileGroupKey.Trim()));
        }
        else
        {
            var filter = string.IsNullOrWhiteSpace(BakeFilter) ? "all" : BakeFilter.Trim();
            args.Append(" -Filter ").Append(QuotePowerShell(filter));
        }

        GD.Print(
            $"TerrainNavigationBaker: invoking bake_bulk_nav_glbs.ps1 → bake_and_export_single_nav.gd "
            + $"(checkpoint={checkpointRel}, filter={(bakeOnly ? "Tile=" + BakeOnlyTileGroupKey : BakeFilter)}, jobs={jobs})");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = args.ToString(),
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var okCount = 0;
        var failCount = 0;
        proc.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            GD.Print(e.Data);
            var done = Regex.Match(e.Data, @"Done\. ok=(\d+) fail=(\d+)");
            if (done.Success)
            {
                okCount = int.Parse(done.Groups[1].Value);
                failCount = int.Parse(done.Groups[2].Value);
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                GD.PrintErr(e.Data);
            }
        };

        try
        {
            if (!proc.Start())
            {
                GD.PushError("TerrainNavigationBaker: failed to start powershell.exe");
                return 0;
            }
        }
        catch (Exception ex)
        {
            GD.PushError($"TerrainNavigationBaker: {ex.Message}");
            return 0;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            GD.PushError(
                $"TerrainNavigationBaker: bake orchestrator exited {proc.ExitCode} "
                + $"(ok={okCount} fail={failCount}). See {outDir}");
        }
        else
        {
            GD.Print(
                $"TerrainNavigationBaker: done ok={okCount} fail={failCount} → {NavMeshResourcesDirectory} "
                + $"(preview/logs: {outDir})");
        }

        // Drop any regions TerrainNavMeshRuntime already registered from the old files on disk.
        TerrainNavMeshRuntime.Invalidate();
        return okCount;
    }

    /// <summary>
    ///     Unused by the script-backed bake. Kept so existing scenes/exports do not break; enabling it
    ///     only warns — regions are not persisted into the scene.
    /// </summary>
    [Export]
    public bool PersistRegionsInScene { get; set; }

    // Retained for inspector/scene compatibility and TerrainNavMeshRuntime docs. Authoritative agent
    // radius / carve params live in bake_and_export_single_nav.gd + CheckpointJsonPath.
    [Export] public NodePath TerrainGridFillPath { get; set; } = "../TerrainGrid";
    [Export] public NodePath TerrainObjectsFillPath { get; set; } = "../TerrainObjects";
    [Export] public string MapBinPath { get; set; } = "res://Godot/Terrain/map.txt";
    [Export] public float TileSizeWorld { get; set; } = 100f;
    [Export] public Vector3 TerrainWorldOrigin { get; set; } = new(-4000f, 0f, -4000f);
    [Export] public float CellSize { get; set; } = 0.1f;
    [Export] public float CellHeight { get; set; } = 0.1f;
    [Export] public float AgentRadius { get; set; } = 0.25f;
    [Export] public float AgentHeight { get; set; } = 1.8f;
    [Export] public float AgentMaxClimb { get; set; } = 0.3f;
    [Export] public float AgentMaxSlope { get; set; } = 70f;
    [Export] public float RegionMinSize { get; set; } = 4f;
    [Export] public float EdgeMaxLength { get; set; } = 12f;
    [Export] public float EdgeMaxError { get; set; } = 1.3f;
    [Export] public float DetailSampleDistance { get; set; } = 6f;
    [Export] public float ObstructionGridCellSize { get; set; } = 0.25f;

    private static string QuotePowerShell(string path)
    {
        return "'" + path.Replace("'", "''") + "'";
    }
}
