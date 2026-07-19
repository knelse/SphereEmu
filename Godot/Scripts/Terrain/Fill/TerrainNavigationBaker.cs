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
///     <c>Tools/bake_bulk_nav_glbs.ps1</c> → <c>Tools/bake_and_export_single_nav.gd</c>,
///     then (when <see cref="BakeIndoorNav" />) indoor cluster nav under
///     <see cref="TerrainNavMeshRuntime.IndoorNavMeshResourcesDirectory" /> via
///     <c>Tools/export_all_indoor_clusters.ps1 -WriteNavRes</c>.
///     Outdoor GDScript owns castle/town carve, arch portals, welds, and prune — do not reimplement
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
    ///     When true (default), after outdoor bake also run indoor cluster Recast bake
    ///     (<c>-WriteNavRes -SkipPreviewGlb</c>). Skipped automatically when
    ///     <see cref="BakeOnlyTileGroupKey" /> is set (single outdoor group rebake).
    /// </summary>
    [Export]
    public bool BakeIndoorNav { get; set; } = true;

    /// <summary>
    ///     Log/manifest directory for the orchestrator (not GLB output). Empty →
    ///     <c>Tools/_nav_bake_project_out</c> under the project root.
    /// </summary>
    [Export]
    public string PreviewOutDirectory { get; set; } = "";

    [ExportToolButton("Bake terrain navigation")]
    public Callable BakeTerrainNavigationButton => Callable.From(() => BakeTerrainNavigation());

    /// <summary>
    ///     Runs the checkpointed headless outdoor bake, then indoor cluster nav when enabled.
    ///     Returns outdoor OK group count from the outdoor orchestrator.
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

        var outdoorOk = RunPowerShell(
            repoRoot,
            args.ToString(),
            donePattern: @"Done\. ok=(\d+) fail=(\d+)",
            out var outdoorFail,
            out var outdoorExit);
        if (outdoorExit != 0)
        {
            GD.PushError(
                $"TerrainNavigationBaker: outdoor bake exited {outdoorExit} "
                + $"(ok={outdoorOk} fail={outdoorFail}). See {outDir}");
        }
        else
        {
            GD.Print(
                $"TerrainNavigationBaker: outdoor done ok={outdoorOk} fail={outdoorFail} → {NavMeshResourcesDirectory} "
                + $"(preview/logs: {outDir})");
        }

        var runIndoor = BakeIndoorNav && !bakeOnly && outdoorExit == 0;
        if (BakeIndoorNav && bakeOnly)
        {
            GD.Print(
                "TerrainNavigationBaker: skipping indoor nav bake (BakeOnlyTileGroupKey set). "
                + "Clear BakeOnlyTileGroupKey or bake indoor via export_all_indoor_clusters.ps1 -WriteNavRes.");
        }
        else if (runIndoor)
        {
            var indoorOk = BakeIndoorNavigationMeshes(repoRoot, jobs, outDir);
            GD.Print($"TerrainNavigationBaker: indoor nav bake ok={indoorOk}");
        }

        // Drop any regions TerrainNavMeshRuntime already registered from the old files on disk.
        TerrainNavMeshRuntime.Invalidate();
        return outdoorOk;
    }

    /// <summary>
    ///     Bakes indoor cluster NavigationMesh resources via
    ///     <c>export_all_indoor_clusters.ps1 -WriteNavRes -SkipPreviewGlb</c>.
    /// </summary>
    public int BakeIndoorNavigationMeshes(string? repoRoot = null, int jobs = 0, string? logOutDir = null)
    {
        repoRoot ??= ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        var indoorPs1 = Path.Combine(repoRoot, "Tools", "export_all_indoor_clusters.ps1");
        if (!File.Exists(indoorPs1))
        {
            GD.PushError($"TerrainNavigationBaker: missing indoor bake script: {indoorPs1}");
            return 0;
        }

        if (jobs <= 0)
        {
            jobs = MaxConcurrentBakeJobs > 0
                ? MaxConcurrentBakeJobs
                : global::System.Environment.ProcessorCount;
        }

        logOutDir ??= Path.Combine(repoRoot, "Tools", "_nav_bake_indoor_out");
        Directory.CreateDirectory(logOutDir);

        var navResAbs = Path.GetFullPath(
            Path.Combine(repoRoot, "Godot", "Terrain", "GeneratedIndoorNavMeshes"));
        Directory.CreateDirectory(navResAbs);

        var args = new StringBuilder();
        args.Append("-NoProfile -ExecutionPolicy Bypass -File ");
        args.Append(QuotePowerShell(indoorPs1));
        args.Append(" -Out ").Append(QuotePowerShell(logOutDir));
        args.Append(" -WriteNavRes -SkipPreviewGlb -SkipManifestRebuild");
        args.Append(" -NavResDir ").Append(QuotePowerShell(navResAbs));
        args.Append(" -Jobs ").Append(jobs);

        GD.Print(
            "TerrainNavigationBaker: invoking export_all_indoor_clusters.ps1 -WriteNavRes "
            + $"(jobs={jobs}, nav={navResAbs})");

        var ok = RunPowerShell(
            repoRoot,
            args.ToString(),
            donePattern: @"done ok=(\d+) fail=(\d+)",
            out var fail,
            out var exitCode);
        if (exitCode != 0)
        {
            GD.PushError(
                $"TerrainNavigationBaker: indoor bake exited {exitCode} (ok={ok} fail={fail}). See {logOutDir}");
        }

        return ok;
    }

    private static int RunPowerShell(
        string workingDirectory,
        string arguments,
        string donePattern,
        out int failCount,
        out int exitCode)
    {
        failCount = 0;
        var okCount = 0;
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var localOk = 0;
        var localFail = 0;
        proc.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            GD.Print(e.Data);
            var done = Regex.Match(e.Data, donePattern, RegexOptions.IgnoreCase);
            if (done.Success)
            {
                localOk = int.Parse(done.Groups[1].Value);
                localFail = int.Parse(done.Groups[2].Value);
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
                exitCode = -1;
                return 0;
            }
        }
        catch (Exception ex)
        {
            GD.PushError($"TerrainNavigationBaker: {ex.Message}");
            exitCode = -1;
            return 0;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();
        exitCode = proc.ExitCode;
        okCount = localOk;
        failCount = localFail;
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
