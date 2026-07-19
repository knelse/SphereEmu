# Export walkable previews for indoor base-tile clusters.
# Pipeline is chosen PER CLUSTER by label (unless forced):
#   cci*  → --pre-lb-walkable (simple upward slope; no shell/weld/strips)
#   else  → latest lb* path (outer shell + inward/wing + weld + strips)
# Manifest clusters by mesh-AABB proximity (5m grace), not placement-origin distance.
#
# -WriteNavRes also bakes NavigationMesh .res under NavResDir (outdoor nav frame) + merges index.json.
param(
    [string]$ManifestPath = "D:/1/indoor-clusters-manifest.json",
    [string]$OutRoot = "",
    [int]$MaxClusters = 0,
    [int]$Jobs = 0,
    ## Keep clusters whose label starts with this (e.g. "cci", "lbg"). Empty = all.
    [string]$LabelPrefix = "",
    ## Explicit cluster ids (overrides LabelPrefix when set).
    [int[]]$ClusterIds = @(),
    ## Force ALL matched clusters onto pre-lb walkable (overrides auto).
    [switch]$PreLbWalkable,
    ## Force ALL matched clusters onto latest lb* pipeline (overrides auto / cci default).
    [switch]$ForceLbPipeline,
    [switch]$SkipManifestRebuild,
    ## Bake NavigationMesh .res (+ .nav.json) per cluster into NavResDir.
    [switch]$WriteNavRes,
    ## Directory for indoor nav .res / index.json (project-relative or absolute).
    [string]$NavResDir = "",
    ## Skip preview object/walkable GLBs (only meaningful with -WriteNavRes).
    [switch]$SkipPreviewGlb
)

$ErrorActionPreference = "Stop"
$ToolsDir = $PSScriptRoot
. (Join-Path $ToolsDir "GodotPath.ps1")
$godot = Resolve-GodotExecutable
# Prefer console build so headless logs don't detach under redirected shells.
$godotConsole = $godot -replace '_win64\.exe$', '_win64_console.exe'
if (Test-Path -LiteralPath $godotConsole) { $godot = $godotConsole }
$RepoRoot = Split-Path $ToolsDir -Parent

if ($Jobs -le 0) {
    $Jobs = [Math]::Max(1, [Math]::Min(8, [Environment]::ProcessorCount))
}

if ($SkipPreviewGlb -and -not $WriteNavRes) {
    throw "-SkipPreviewGlb requires -WriteNavRes"
}

if (-not $SkipManifestRebuild) {
    Write-Host "Building cluster manifest..."
    python (Join-Path $ToolsDir "build_indoor_cluster_manifest.py")
    if ($LASTEXITCODE -ne 0) { throw "manifest build failed" }
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $OutRoot) {
    # No spaces: Start-Process ArgumentList does not quote, and Godot splits on spaces.
    $stamp = Get-Date -Format "yyyy-M-d_HH-mm-ss"
    $OutRoot = "D:/1/${stamp}_indoor-clusters"
}
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
$logsDir = Join-Path $OutRoot "_logs"
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

if (-not $NavResDir) {
    $NavResDir = Join-Path $RepoRoot "Godot/Terrain/GeneratedIndoorNavMeshes"
}
elseif ($NavResDir -notmatch '^[A-Za-z]:[\\/]' -and -not $NavResDir.StartsWith("/")) {
    $NavResDir = Join-Path $RepoRoot ($NavResDir -replace '\\', '/')
}
$NavResDir = $NavResDir -replace '\\', '/'
if ($WriteNavRes) {
    New-Item -ItemType Directory -Force -Path $NavResDir | Out-Null
}

$clusters = @($manifest.clusters)
if ($ClusterIds -and $ClusterIds.Count -gt 0) {
    $idSet = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($id in $ClusterIds) { [void]$idSet.Add([int]$id) }
    $clusters = @($clusters | Where-Object { $idSet.Contains([int]$_.id) })
} elseif ($LabelPrefix) {
    $prefix = $LabelPrefix.ToLowerInvariant()
    $clusters = @($clusters | Where-Object {
            ([string]$_.label).ToLowerInvariant().StartsWith($prefix)
        })
}
if ($MaxClusters -gt 0 -and $MaxClusters -lt $clusters.Count) {
    $clusters = $clusters[0..($MaxClusters - 1)]
}
$total = $clusters.Count
if ($total -le 0) {
    throw "No clusters matched (LabelPrefix='$LabelPrefix' ClusterIds=$($ClusterIds -join ','))"
}
if ($PreLbWalkable -and $ForceLbPipeline) {
    throw "Use only one of -PreLbWalkable / -ForceLbPipeline (or neither for auto by label)"
}

function Resolve-PreLbWalkable {
    param(
        [string]$Label,
        [bool]$ForcePreLb,
        [bool]$ForceLb
    )
    if ($ForceLb) { return $false }
    if ($ForcePreLb) { return $true }
    # Auto: cci* → pre-lb*; everything else (lb*, rd_*, …) → latest lb* pipeline.
    return ([string]$Label).ToLowerInvariant().StartsWith("cci")
}

function Write-IndoorNavIndex {
    param([string]$Dir)
    $sidecars = @(Get-ChildItem -LiteralPath $Dir -Filter "cluster_*.nav.json" -ErrorAction SilentlyContinue)
    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($f in $sidecars) {
        try {
            $entries.Add((Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json)) | Out-Null
        } catch {
            Write-Warning "Bad nav sidecar $($f.Name): $($_.Exception.Message)"
        }
    }
    $index = [ordered]@{
        version     = 1
        count       = $entries.Count
        generated   = (Get-Date).ToString("o")
        clusters    = @($entries | Sort-Object { [int]$_.id })
    }
    $indexPath = Join-Path $Dir "index.json"
    ($index | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $indexPath -Encoding UTF8
    Write-Host "Wrote indoor nav index ($($entries.Count) clusters) -> $indexPath"
}

$cciN = @($clusters | Where-Object {
        (Resolve-PreLbWalkable -Label ([string]$_.label) -ForcePreLb $PreLbWalkable -ForceLb $ForceLbPipeline)
    }).Count
$lbN = $total - $cciN
$logPath = Join-Path $OutRoot "_export_log.txt"
$policy = if ($ForceLbPipeline) { "force_lb_pipeline" }
    elseif ($PreLbWalkable) { "force_pre_lb" }
    else { "auto_cci=pre_lb_else=lb_latest" }
"godot=$godot clusters=$total max=$MaxClusters jobs=$Jobs label_prefix=$LabelPrefix policy=$policy pre_lb=$cciN lb_latest=$lbN write_nav_res=$WriteNavRes skip_preview_glb=$SkipPreviewGlb nav_res_dir=$NavResDir mesh_grace=$($manifest.mesh_grace_m)" |
    Set-Content -LiteralPath $logPath -Encoding UTF8

Write-Host "Exporting $total clusters (Jobs=$Jobs, prefix='$LabelPrefix', policy=$policy, pre_lb=$cciN lb_latest=$lbN write_nav=$WriteNavRes) -> $OutRoot"
$swAll = [System.Diagnostics.Stopwatch]::StartNew()

$exportOne = {
    param(
        [string]$Godot,
        [string]$RepoRoot,
        [string]$OutRoot,
        [string]$LogsDir,
        [int]$Index,
        [int]$Total,
        [int]$Id,
        [string]$Label,
        [double]$Cx,
        [double]$Cy,
        [double]$Cz,
        [double]$Radius,
        [int]$Count,
        [string]$Members,
        [bool]$PreLbWalkable,
        [bool]$WriteNavRes,
        [string]$NavResDir,
        [bool]$SkipPreviewGlb
    )

    $outGlb = Join-Path $OutRoot ("cluster_{0}.glb" -f $Id)
    $walkableGlb = Join-Path $OutRoot ("walkable_cluster_{0}.glb" -f $Id)
    $tempWalkable = Join-Path $OutRoot ("cluster_{0}_walkable.glb" -f $Id)
    $tempManifest = Join-Path $OutRoot ("cluster_{0}_manifest.txt" -f $Id)
    $jobLog = Join-Path $LogsDir ("cluster_{0}.log" -f $Id)
    $walkTag = if ($PreLbWalkable) { "pre_lb" } else { "lb_latest" }
    $navResPath = Join-Path $NavResDir ("cluster_{0}.res" -f $Id)
    # Godot ResourceSaver prefers res:// for project resources.
    $navResGodot = ("res://Godot/Terrain/GeneratedIndoorNavMeshes/cluster_{0}.res" -f $Id)
    if ($WriteNavRes) {
        $navAbs = [System.IO.Path]::GetFullPath($navResPath).Replace('\', '/')
        $repoAbs = [System.IO.Path]::GetFullPath($RepoRoot).Replace('\', '/')
        if ($navAbs.StartsWith($repoAbs + "/", [System.StringComparison]::OrdinalIgnoreCase)) {
            $rel = $navAbs.Substring($repoAbs.Length + 1)
            $navResGodot = "res://" + $rel
        } else {
            $navResGodot = $navAbs
        }
    }

    if (-not $Members) {
        $Members = "D:/1/indoor-cluster-members/cluster_{0:D3}.json" -f $Id
    }

    $argList = @(
        "--path", $RepoRoot,
        "--headless",
        "-s", "Tools/export_nearby_objects_glb.gd",
        "--",
        "--center", "$Cx", "$Cy", "$Cz",
        "--radius", "$Radius",
        "--members", $Members,
        "--with-walkable",
        "--indoor-base-only",
        "--cluster-id", "$Id",
        "--out", $outGlb
    )
    if ($PreLbWalkable) {
        $argList += "--pre-lb-walkable"
    }
    if ($WriteNavRes) {
        $argList += @("--write-nav-res", $navResGodot)
    }
    if ($SkipPreviewGlb) {
        $argList += "--skip-preview-glb"
    }
    $argsQuoted = ($argList | ForEach-Object {
            if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
        }) -join " "

    Write-Host ("[{0}/{1}] id={2} n={3} r={4} {5} [{6}]" -f `
        $Index, $Total, $Id, $Count, $Radius, $Label, $walkTag)

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Godot
    $psi.Arguments = $argsQuoted
    $psi.WorkingDirectory = $RepoRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    [void][System.Threading.Tasks.Task]::WaitAll(@($outTask, $errTask))
    $stdout = $outTask.Result
    $stderr = $errTask.Result
    $proc.WaitForExit()
    $code = $proc.ExitCode
    $sw.Stop()

    Set-Content -LiteralPath $jobLog -Value ("exit={0}`n{1}`n{2}" -f $code, $stdout, $stderr) -Encoding utf8

    if (-not $SkipPreviewGlb) {
        if ($code -eq 0 -and (Test-Path -LiteralPath $tempWalkable)) {
            Move-Item -LiteralPath $tempWalkable -Destination $walkableGlb -Force
        }
        if (Test-Path -LiteralPath $tempManifest) {
            Remove-Item -LiteralPath $tempManifest -Force
        }
    }

    $navOk = (-not $WriteNavRes) -or (Test-Path -LiteralPath $navResPath)
    if ($SkipPreviewGlb) {
        $ok = ($code -eq 0) -and $navOk
    } else {
        $ok = ($code -eq 0) -and (Test-Path -LiteralPath $outGlb) -and (Test-Path -LiteralPath $walkableGlb) -and $navOk
    }
    if ($ok) {
        Write-Host ("  ok id={0} ({1:n1}s)" -f $Id, $sw.Elapsed.TotalSeconds)
    } else {
        Write-Warning ("FAILED id={0} exit={1} out={2} walkable={3} nav={4}" -f `
            $Id, $code, (Test-Path -LiteralPath $outGlb), (Test-Path -LiteralPath $walkableGlb), $navOk)
    }

    return [pscustomobject]@{
        Id       = $Id
        Exit     = $code
        Ok       = $ok
        Seconds  = $sw.Elapsed.TotalSeconds
        Out      = $outGlb
        Walkable = $walkableGlb
        Walk     = $walkTag
        NavRes   = $navResPath
    }
}

$sessionState = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault()
$pool = [RunspaceFactory]::CreateRunspacePool(1, $Jobs, $sessionState, $Host)
$pool.Open()

$workers = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $total; $i++) {
    $c = $clusters[$i]
    $members = [string]$c.members
    if (-not $members) {
        $members = "D:/1/indoor-cluster-members/cluster_{0:D3}.json" -f ([int]$c.id)
    }
    $usePreLb = Resolve-PreLbWalkable `
        -Label ([string]$c.label) `
        -ForcePreLb ([bool]$PreLbWalkable) `
        -ForceLb ([bool]$ForceLbPipeline)
    $ps = [PowerShell]::Create()
    $ps.RunspacePool = $pool
    [void]$ps.AddScript($exportOne).AddParameters(@{
            Godot          = $godot
            RepoRoot       = $RepoRoot
            OutRoot        = $OutRoot
            LogsDir        = $logsDir
            Index          = $i + 1
            Total          = $total
            Id             = [int]$c.id
            Label          = [string]$c.label
            Cx             = [double]$c.center[0]
            Cy             = [double]$c.center[1]
            Cz             = [double]$c.center[2]
            Radius         = [double]$c.radius
            Count          = [int]$c.count
            Members        = $members
            PreLbWalkable  = [bool]$usePreLb
            WriteNavRes    = [bool]$WriteNavRes
            NavResDir      = $NavResDir
            SkipPreviewGlb = [bool]$SkipPreviewGlb
        })
    $workers.Add([pscustomobject]@{ PS = $ps; Handle = $ps.BeginInvoke(); Id = [int]$c.id }) | Out-Null
}

$ok = 0
$fail = 0
$results = New-Object System.Collections.Generic.List[object]
foreach ($w in $workers) {
    try {
        $r = $w.PS.EndInvoke($w.Handle)
        if ($r -is [System.Array] -and $r.Count -gt 0) { $r = $r[0] }
        $results.Add($r) | Out-Null
        $line = "id=$($r.Id) exit=$($r.Exit) ok=$($r.Ok) walk=$($r.Walk) secs=$([Math]::Round($r.Seconds, 2))"
        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
        if ($r.Ok) { $ok++ } else { $fail++ }
    } catch {
        $fail++
        Add-Content -LiteralPath $logPath -Value ("id={0} EXCEPTION {1}" -f $w.Id, $_.Exception.Message) -Encoding UTF8
        Write-Warning ("EXCEPTION id={0}: {1}" -f $w.Id, $_.Exception.Message)
    } finally {
        $w.PS.Dispose()
    }
}

$pool.Close()
$pool.Dispose()
$swAll.Stop()

if ($WriteNavRes -and $ok -gt 0) {
    Write-IndoorNavIndex -Dir $NavResDir
}

$summary = "done ok=$ok fail=$fail jobs=$Jobs elapsed_s=$([int]$swAll.Elapsed.TotalSeconds) out=$OutRoot write_nav_res=$WriteNavRes"
Add-Content -LiteralPath $logPath -Value $summary -Encoding UTF8
Write-Host $summary
if ($fail -gt 0) { exit 1 }
exit 0
