using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using SphServer.Godot.Scripts.Navigation;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
/// Outdoor GDScript owns castle/town carve, arch portals, welds, and prune. Recast carving is not
/// done here. Checkpoint params come from CheckpointJsonPath (explicit_env plus per-group
/// env_overrides)
/// </summary>
[Tool]
public partial class TerrainNavigationBaker : Node3D
{
    public const string NavigationRegionsRootName = "TerrainNavigation";

    [Export]
    public string NavMeshResourcesDirectory { get; set; } = "";

    /// <summary>
    /// CC checkpoint: explicit_env baseline plus per-group overrides (undercroft, archway)
    /// </summary>
    [Export]
    public string CheckpointJsonPath { get; set; } = "Tools/nav_bake_checkpoint_cc.json";

    /// <summary>
    /// plan_bulk_nav_bakes.py filter (all, cc, town). Ignored when BakeOnlyTileGroupKey is set
    /// </summary>
    [Export]
    public string BakeFilter { get; set; } = "all";

    /// <summary>
    /// 0 means processor count
    /// </summary>
    [Export]
    public int MaxConcurrentBakeJobs { get; set; }

    /// <summary>
    /// Towns and CC 2x2 expand one key into four tiles (Town4_00_00 or Town4_occ00)
    /// </summary>
    [Export]
    public string BakeOnlyTileGroupKey { get; set; } = "";

    /// <summary>
    /// Also runs indoor cluster Recast (-WriteNavRes -SkipPreviewGlb). Skipped when
    /// BakeOnlyTileGroupKey is set
    /// </summary>
    [Export]
    public bool BakeIndoorNav { get; set; } = true;

    /// <summary>
    /// Orchestrator log and manifest, not GLB output. Empty uses Tools/_nav_bake_project_out
    /// </summary>
    [Export]
    public string PreviewOutDirectory { get; set; } = "";

    [ExportToolButton ("Bake terrain navigation")]
    public Callable BakeTerrainNavigationButton => Callable.From (() => BakeTerrainNavigation ());

    public int BakeTerrainNavigation ()
    {
        if (PersistRegionsInScene)
        {
            GD.PushWarning (
                "TerrainNavigationBaker: PersistRegionsInScene is ignored — the bake script writes .res files only. "
                + "Use TerrainNavMeshRuntime to load them.");
        }

        var repoRoot = ProjectSettings.GlobalizePath ("res://").TrimEnd ('/', '\\');
        var ps1 = Path.Combine (repoRoot, "Tools", "bake_bulk_nav_glbs.ps1");
        if (!File.Exists (ps1))
        {
            GD.PushError ($"TerrainNavigationBaker: missing orchestrator script: {ps1}");
            return 0;
        }

        var checkpointRel = (CheckpointJsonPath ?? "").Replace ('\\', '/').TrimStart ('/');
        var checkpointAbs = Path.GetFullPath (Path.Combine (repoRoot, checkpointRel));
        if (!File.Exists (checkpointAbs))
        {
            GD.PushError ($"TerrainNavigationBaker: checkpoint not found: {checkpointAbs}");
            return 0;
        }

        var outDir = string.IsNullOrWhiteSpace (PreviewOutDirectory)
            ? Path.Combine (repoRoot, "Tools", "_nav_bake_project_out")
            : PreviewOutDirectory.StartsWith ("res://", StringComparison.Ordinal)
                ? ProjectSettings.GlobalizePath (PreviewOutDirectory)
                : PreviewOutDirectory;
        Directory.CreateDirectory (outDir);

        var jobs = MaxConcurrentBakeJobs > 0
            ? MaxConcurrentBakeJobs
            : global::System.Environment.ProcessorCount;

        var args = new StringBuilder ();
        args.Append ("-NoProfile -ExecutionPolicy Bypass -File ");
        args.Append (QuotePowerShell (ps1));
        args.Append (" -Out ").Append (QuotePowerShell (outDir));
        args.Append (" -CheckpointJson ").Append (QuotePowerShell (checkpointAbs));
        args.Append (" -WriteRes"); // .res only (orchestrator skips GLB unless -ExportGlb)
        args.Append (" -Jobs ").Append (jobs);

        var bakeOnly = !string.IsNullOrWhiteSpace (BakeOnlyTileGroupKey);
        if (bakeOnly)
        {
            // The plan still comes from the map, so -Tile expands a 1x1 key into a 2x2 town or CC
            // group
            args.Append (" -Filter all");
            args.Append (" -Tile ").Append (QuotePowerShell (BakeOnlyTileGroupKey.Trim ()));
        }
        else
        {
            var filter = string.IsNullOrWhiteSpace (BakeFilter) ? "all" : BakeFilter.Trim ();
            args.Append (" -Filter ").Append (QuotePowerShell (filter));
        }

        GD.Print (
            $"TerrainNavigationBaker: invoking bake_bulk_nav_glbs.ps1 → bake_and_export_single_nav.gd "
            + $"(checkpoint={checkpointRel}, filter={(bakeOnly ? "Tile=" + BakeOnlyTileGroupKey : BakeFilter)}, jobs={jobs})");

        var outdoorOk = RunPowerShell (
            repoRoot,
            args.ToString (),
            donePattern: @"Done\. ok=(\d+) fail=(\d+)",
            out var outdoorFail,
            out var outdoorExit);
        if (outdoorExit != 0)
        {
            GD.PushError (
                $"TerrainNavigationBaker: outdoor bake exited {outdoorExit} "
                + $"(ok={outdoorOk} fail={outdoorFail}). See {outDir}");
        }
        else
        {
            GD.Print (
                $"TerrainNavigationBaker: outdoor done ok={outdoorOk} fail={outdoorFail} → {NavMeshResourcesDirectory} "
                + $"(preview/logs: {outDir})");
        }

        var runIndoor = BakeIndoorNav && !bakeOnly && outdoorExit == 0;
        if (BakeIndoorNav && bakeOnly)
        {
            GD.Print (
                "TerrainNavigationBaker: skipping indoor nav bake (BakeOnlyTileGroupKey set). "
                + "Clear BakeOnlyTileGroupKey or bake indoor via export_all_indoor_clusters.ps1 -WriteNavRes.");
        }
        else if (runIndoor)
        {
            var indoorOk = BakeIndoorNavigationMeshes (repoRoot, jobs, outDir);
            GD.Print ($"TerrainNavigationBaker: indoor nav bake ok={indoorOk}");
        }

        // Drop regions TerrainNavMeshRuntime already registered from the old files on disk
        TerrainNavMeshRuntime.Invalidate ();
        return outdoorOk;
    }

    public int BakeIndoorNavigationMeshes (string? repoRoot = null, int jobs = 0, string? logOutDir = null)
    {
        repoRoot ??= ProjectSettings.GlobalizePath ("res://").TrimEnd ('/', '\\');
        var indoorPs1 = Path.Combine (repoRoot, "Tools", "export_all_indoor_clusters.ps1");
        if (!File.Exists (indoorPs1))
        {
            GD.PushError ($"TerrainNavigationBaker: missing indoor bake script: {indoorPs1}");
            return 0;
        }

        if (jobs <= 0)
        {
            jobs = MaxConcurrentBakeJobs > 0
                ? MaxConcurrentBakeJobs
                : global::System.Environment.ProcessorCount;
        }

        logOutDir ??= Path.Combine (repoRoot, "Tools", "_nav_bake_indoor_out");
        Directory.CreateDirectory (logOutDir);

        var navResAbs = Path.GetFullPath (
            Path.Combine (repoRoot, "GodotAssetSource", "TerrainBake", "GeneratedIndoorNavMeshes"));
        Directory.CreateDirectory (navResAbs);

        var args = new StringBuilder ();
        args.Append ("-NoProfile -ExecutionPolicy Bypass -File ");
        args.Append (QuotePowerShell (indoorPs1));
        args.Append (" -Out ").Append (QuotePowerShell (logOutDir));
        args.Append (" -WriteNavRes -SkipPreviewGlb -SkipManifestRebuild");
        args.Append (" -NavResDir ").Append (QuotePowerShell (navResAbs));
        args.Append (" -Jobs ").Append (jobs);

        GD.Print (
            "TerrainNavigationBaker: invoking export_all_indoor_clusters.ps1 -WriteNavRes "
            + $"(jobs={jobs}, nav={navResAbs})");

        var ok = RunPowerShell (
            repoRoot,
            args.ToString (),
            donePattern: @"done ok=(\d+) fail=(\d+)",
            out var fail,
            out var exitCode);
        if (exitCode != 0)
        {
            GD.PushError (
                $"TerrainNavigationBaker: indoor bake exited {exitCode} (ok={ok} fail={fail}). See {logOutDir}");
        }

        return ok;
    }

    private static int RunPowerShell (
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
            if (string.IsNullOrEmpty (e.Data))
            {
                return;
            }

            GD.Print (e.Data);
            var done = Regex.Match (e.Data, donePattern, RegexOptions.IgnoreCase);
            if (done.Success)
            {
                localOk = int.Parse (done.Groups[1].Value);
                localFail = int.Parse (done.Groups[2].Value);
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty (e.Data))
            {
                GD.PrintErr (e.Data);
            }
        };

        try
        {
            if (!proc.Start ())
            {
                GD.PushError ("TerrainNavigationBaker: failed to start powershell.exe");
                exitCode = -1;
                return 0;
            }
        }
        catch (Exception ex)
        {
            GD.PushError ($"TerrainNavigationBaker: {ex.Message}");
            exitCode = -1;
            return 0;
        }

        proc.BeginOutputReadLine ();
        proc.BeginErrorReadLine ();
        proc.WaitForExit ();
        exitCode = proc.ExitCode;
        okCount = localOk;
        failCount = localFail;
        return okCount;
    }

    /// <summary>
    /// Scenes still export this. Enabling it only warns. Regions are not written into the scene
    /// </summary>
    [Export]
    public bool PersistRegionsInScene { get; set; }

    // Inspector and scene compatibility. Agent radius and carve params live in
    // bake_and_export_single_nav.gd and CheckpointJsonPath
    [Export] public NodePath TerrainGridFillPath { get; set; } = "../TerrainGrid";
    [Export] public NodePath TerrainObjectsFillPath { get; set; } = "../TerrainObjects";
    [Export] public string MapBinPath { get; set; } = "res://Godot/Terrain/map.txt";
    [Export] public float TileSizeWorld { get; set; } = 100f;
    [Export] public Vector3 TerrainWorldOrigin { get; set; } = new (-4000f, 0f, -4000f);
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

    private static string QuotePowerShell (string path)
    {
        return "'" + path.Replace ("'", "''") + "'";
    }
}
