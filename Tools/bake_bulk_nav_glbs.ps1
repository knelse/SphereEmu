# Bakes nav for every map occurrence of selected tile families.
# Default: production .res under Godot/Terrain/GeneratedNavMeshes (no GLB).
# 2x2 masters (variant corners 00/01/10/11 on a contiguous block) are combined;
# everything else is 1x1. Plan is generated from Godot/Terrain/map.txt.
#
# Usage (repo root):
#   .\Tools\bake_bulk_nav_glbs.ps1
#   .\Tools\bake_bulk_nav_glbs.ps1 -Out D:/1/my-bulk
#   .\Tools\bake_bulk_nav_glbs.ps1 -PlanOnly
#   .\Tools\bake_bulk_nav_glbs.ps1 -Jobs 8
#   .\Tools\bake_bulk_nav_glbs.ps1 -WriteRes   # production .res only (no GLB)
#   .\Tools\bake_bulk_nav_glbs.ps1 -ExportGlb  # preview GLBs under -Out (optional)
#   .\Tools\bake_bulk_nav_glbs.ps1 -Filter cc -CheckpointJson Tools/nav_bake_checkpoint_cc.json -WriteRes

param(
    [string]$Out = "",
    [string]$PlanJson = "",
    [string]$Filter = "all",
    # When set, apply checkpoint explicit_env as baseline + groups[].env_overrides per group
    # (matched by group name or any tile key). Godot is spawned with a private env block so
    # parallel jobs cannot race on process-wide NAV_EXPERIMENT_* vars.
    [string]$CheckpointJson = "",
    # Restrict to plan groups whose name equals this, or whose tiles[] contain this key
    # (e.g. -Tile Town4_00_00 expands to the full Town4 2x2 group).
    [string]$Tile = "",
    # Restrict to plan groups whose name is in this list (comma-separated or array).
    [string[]]$GroupName = @(),
    # Use an existing PlanJson as-is (do not regenerate from map.txt).
    [switch]$SkipPlan,
    [switch]$PlanOnly,
    # Write production Godot/Terrain/GeneratedNavMeshes/*.res (default path; no GLB unless -ExportGlb).
    [switch]$WriteRes,
    # Also export preview GLBs under -Out (slow; not needed for TerrainNavMeshRuntime).
    [switch]$ExportGlb,
    [switch]$ColorizeRegions,
    [int]$StartIndex = 0,
    [int]$Limit = 0,
    [int]$Jobs = 0
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "GodotPath.ps1")
$godot = (Resolve-GodotExecutable) -replace '_win64\.exe$', '_win64_console.exe'
if (-not (Test-Path $godot)) { $godot = Resolve-GodotExecutable }
if (-not $godot) { throw "Godot executable not found." }

# Production default: WriteRes without GLB. -ExportGlb / -ColorizeRegions opt into previews.
if (-not $WriteRes -and -not $ExportGlb -and -not $PlanOnly) {
    $WriteRes = $true
}
if ($ColorizeRegions -and -not $ExportGlb) {
    Write-Host "ColorizeRegions requires preview GLBs; enabling -ExportGlb"
    $ExportGlb = $true
}
if (-not $Out) {
    $Out = "D:/1/{0} bulk-nav" -f (Get-Date -Format "yyyy-M-d H-mm-ss")
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$navResDir = Join-Path $RepoRoot "Godot\Terrain\GeneratedNavMeshes"

$planPy = Join-Path $PSScriptRoot "plan_bulk_nav_bakes.py"
if (-not (Test-Path $planPy)) {
    throw "Missing $planPy - regenerate the bake plan script first."
}
if (-not $PlanJson) { $PlanJson = Join-Path $Out "bulk_nav_bake_plan.json" }
if ($SkipPlan) {
    if (-not (Test-Path -LiteralPath $PlanJson)) {
        throw "SkipPlan set but PlanJson missing: $PlanJson"
    }
    Write-Host "SkipPlan: using existing plan $PlanJson"
}
else {
    Write-Host "Planning groups from map.txt (filter=$Filter) -> $PlanJson"
    python $planPy $PlanJson $Filter | Tee-Object -FilePath (Join-Path $Out "plan.log")
}

# ConvertFrom-Json may emit a JSON array as one pipeline object; unwrap to a flat list.
$parsed = Get-Content -Raw -Path $PlanJson | ConvertFrom-Json
if ($null -eq $parsed) {
    $groups = @()
}
elseif ($parsed -is [System.Array]) {
    $groups = [object[]]$parsed
}
else {
    $groups = @($parsed)
}
# Guard against accidental single-nested array (PS NoEnumerate / @() wrapping).
while (
    $groups.Count -eq 1 -and
    $null -ne $groups[0] -and
    $groups[0] -is [System.Array] -and
    $groups[0].Count -gt 0 -and
    $null -ne $groups[0][0] -and
    ($groups[0][0].PSObject.Properties.Name -contains "name")
) {
    $groups = [object[]]$groups[0]
}
if ($Tile) {
    $tileKey = [string]$Tile
    $groups = @($groups | Where-Object {
            ([string]$_.name -eq $tileKey) -or (@($_.tiles) -contains $tileKey)
        })
    Write-Host "Tile filter '$tileKey' -> $($groups.Count) group(s)"
    if ($groups.Count -eq 0) {
        throw "No plan group matches -Tile $tileKey (check map plan / tile key spelling)."
    }
}
if ($GroupName -and $GroupName.Count -gt 0) {
    $want = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($gn in $GroupName) {
        foreach ($part in ([string]$gn).Split(@(',', ';'), [System.StringSplitOptions]::RemoveEmptyEntries)) {
            [void]$want.Add($part.Trim())
        }
    }
    $groups = @($groups | Where-Object { $want.Contains([string]$_.name) })
    Write-Host ("GroupName filter ({0}) -> {1} group(s)" -f $want.Count, $groups.Count)
    if ($groups.Count -eq 0) {
        throw "No plan group matches -GroupName."
    }
}
if ($StartIndex -gt 0) { $groups = @($groups | Select-Object -Skip $StartIndex) }
if ($Limit -gt 0) { $groups = @($groups | Select-Object -First $Limit) }
Write-Host "Loaded $($groups.Count) bake group(s) from plan."

# Baseline env (cc_baseline_v1). Overridden by checkpoint.explicit_env when present.
$baselineEnv = [ordered]@{
    NAV_EXPERIMENT_ADDFACES      = "2"
    NAV_EXPERIMENT_PRUNE_ISLANDS = "1"
    NAV_EXPERIMENT_AUTHORED_Y    = "1"
    NAV_EXPERIMENT_REGION_MIN    = "14"
    NAV_EXPERIMENT_SLOPE_DEG     = "55"
    NAV_EXPERIMENT_BUILDING_FILL = "1"
    NAV_EXPERIMENT_FILL_INCLUDE  = "Town_ph00"
}

# Checkpoint: map group name / tile key -> env_overrides (+ param_profile for logs).
$checkpointOverrides = @{} # name-or-tile -> Hashtable[string,string]
$checkpointProfiles = @{}  # name-or-tile -> string
if ($CheckpointJson) {
    if (-not (Test-Path -LiteralPath $CheckpointJson)) {
        throw "CheckpointJson not found: $CheckpointJson"
    }
    $cp = Get-Content -Raw -Path $CheckpointJson | ConvertFrom-Json
    if ($null -ne $cp.explicit_env) {
        foreach ($p in $cp.explicit_env.PSObject.Properties) {
            $baselineEnv[[string]$p.Name] = [string]$p.Value
        }
        Write-Host ("Checkpoint baseline: explicit_env from {0} ({1} keys)" -f `
                $CheckpointJson, @($cp.explicit_env.PSObject.Properties).Count)
    }
    foreach ($cg in @($cp.groups)) {
        $cname = [string]$cg.name
        $profile = [string]$cg.param_profile
        $ov = @{}
        if ($null -ne $cg.env_overrides) {
            foreach ($p in $cg.env_overrides.PSObject.Properties) {
                $ov[[string]$p.Name] = [string]$p.Value
            }
        }
        $checkpointProfiles[$cname] = $profile
        $checkpointOverrides[$cname] = $ov
        # Also index by each tile key so -Tile Town4_00_00 / single-cell lookups resolve.
        foreach ($t in @($cg.tiles)) {
            $tk = [string]$t
            if (-not $tk) { continue }
            $checkpointProfiles[$tk] = $profile
            $checkpointOverrides[$tk] = $ov
        }
    }
    $tunedGroups = @(
        @($cp.groups) | Where-Object {
            $null -ne $_.env_overrides -and @($_.env_overrides.PSObject.Properties).Count -gt 0
        }
    ).Count
    Write-Host ("Checkpoint: {0} ({1} group(s) with env_overrides)" -f $CheckpointJson, $tunedGroups)
}

if ($PlanOnly) {
    Write-Host "PlanOnly: $($groups.Count) groups -> $Out"
    exit 0
}

if ($Jobs -le 0) {
    $Jobs = [Environment]::ProcessorCount
}
if ($Jobs -lt 1) { $Jobs = 1 }
if ($Jobs -gt $groups.Count) { $Jobs = [Math]::Max(1, $groups.Count) }

$logsDir = Join-Path $Out "job_logs"
$resultsDir = Join-Path $Out "job_results"
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$manifestPath = Join-Path $Out "manifest.csv"
"name,kind,family,occ,tiles,artifact,status,seconds,param_profile" | Set-Content -Path $manifestPath -Encoding utf8

$total = $groups.Count
Write-Host "Godot: $godot"
Write-Host "OUT: $Out"
Write-Host "Groups: $total  Jobs=$Jobs  WriteRes=$WriteRes  ExportGlb=$ExportGlb"
Write-Host "Parallel Godot workers: $Jobs"
if ($WriteRes) {
    Write-Host "Production .res -> $navResDir"
}

$bakeScript = {
    param(
        [string]$Godot,
        [string]$RepoRoot,
        [string]$Out,
        [string]$LogsDir,
        [string]$ResultsDir,
        [int]$Index,
        [int]$Total,
        [string]$Name,
        [string]$Kind,
        [string]$Family,
        [object]$Occ,
        [string[]]$Tiles,
        [bool]$WriteRes,
        [bool]$ExportGlb,
        [string]$NavResDir,
        [hashtable]$EnvOverrides,
        [string]$ParamProfile,
        [hashtable]$BaselineEnv
    )

    # Baseline (checkpoint explicit_env or cc_baseline_v1 defaults) + per-group overrides.
    # Applied only to the Godot child process so parallel jobs cannot clobber each other.
    $childEnv = @{}
    if ($null -ne $BaselineEnv) {
        foreach ($k in $BaselineEnv.Keys) { $childEnv[[string]$k] = [string]$BaselineEnv[$k] }
    }
    if ($null -ne $EnvOverrides) {
        foreach ($k in $EnvOverrides.Keys) { $childEnv[[string]$k] = [string]$EnvOverrides[$k] }
    }

    $tileArgs = New-Object System.Collections.Generic.List[string]
    foreach ($t in $Tiles) {
        $tileArgs.Add("--tile") | Out-Null
        $tileArgs.Add([string]$t) | Out-Null
    }

    $argList = New-Object System.Collections.Generic.List[string]
    foreach ($a in @("--path", $RepoRoot, "--headless", "-s", "Tools/bake_and_export_single_nav.gd", "--")) {
        $argList.Add($a) | Out-Null
    }
    foreach ($a in $tileArgs) { $argList.Add($a) | Out-Null }
    foreach ($a in @("--combined", "--combined-name", $Name)) {
        $argList.Add($a) | Out-Null
    }
    # Production: --bake-only writes GeneratedNavMeshes/*.res and skips GLB export.
    # Preview: --out + optional --write-res still emits side-by-side GLBs.
    if ($ExportGlb) {
        $argList.Add("--out") | Out-Null
        $argList.Add($Out) | Out-Null
        if ($WriteRes) { $argList.Add("--write-res") | Out-Null }
    }
    else {
        $argList.Add("--bake-only") | Out-Null
    }

    $ovNote = ""
    if ($null -ne $EnvOverrides -and $EnvOverrides.Count -gt 0) {
        $ovNote = " overrides=" + (($EnvOverrides.GetEnumerator() | ForEach-Object { "{0}={1}" -f $_.Key, $_.Value }) -join ",")
    }
    Write-Host ("=== [{0}/{1}] {2} ({3}, {4} tile(s), profile={5}){6} ===" -f `
        $Index, $Total, $Name, $Kind, $Tiles.Count, $ParamProfile, $ovNote)

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Godot
    $psi.Arguments = (($argList | ForEach-Object {
                if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
            }) -join " ")
    $psi.WorkingDirectory = $RepoRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    # Start from a clean NAV_EXPERIMENT_* / DIAG_* slate in the child (process-private env).
    $envBag = $psi.EnvironmentVariables
    $toRemove = @()
    foreach ($key in @($envBag.Keys)) {
        if ($key -like "NAV_EXPERIMENT_*" -or $key -like "DIAG_*") { $toRemove += $key }
    }
    foreach ($key in $toRemove) { $envBag.Remove($key) }
    foreach ($k in $childEnv.Keys) { $envBag[[string]$k] = [string]$childEnv[$k] }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    # Read stdout/stderr concurrently — sequential ReadToEnd can deadlock when buffers fill.
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    [void][System.Threading.Tasks.Task]::WaitAll(@($outTask, $errTask))
    $stdout = $outTask.Result
    $stderr = $errTask.Result
    $proc.WaitForExit()
    $exit = $proc.ExitCode
    $sw.Stop()

    $jobLog = Join-Path $LogsDir ("{0}.log" -f $Name)
    $logBody = ("profile={0}`nenv={1}`n`n{2}`n{3}" -f `
        $ParamProfile,
        (($childEnv.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0}={1}" -f $_.Key, $_.Value }) -join "`n"),
        $stdout,
        $stderr)
    Set-Content -Path $jobLog -Value $logBody -Encoding utf8

    $artifact = ""
    $status = "ok"
    if ($ExportGlb) {
        $artifact = Join-Path $Out "$Name.glb"
        if ($exit -ne 0 -or -not (Test-Path -LiteralPath $artifact)) {
            $status = "FAIL"
            Write-Warning ("FAILED {0} (exit={1})" -f $Name, $exit)
        }
        else {
            $secs = "{0:n1}" -f $sw.Elapsed.TotalSeconds
            $kb = "{0:n0}" -f ((Get-Item -LiteralPath $artifact).Length / 1KB)
            Write-Host ("OK {0} ({1}s, {2} KB glb)" -f $Name, $secs, $kb)
        }
    }
    else {
        $missing = @()
        foreach ($t in $Tiles) {
            $resPath = Join-Path $NavResDir ("{0}.res" -f $t)
            if (-not (Test-Path -LiteralPath $resPath)) { $missing += $t }
        }
        $artifact = if ($Tiles.Count -gt 0) {
            Join-Path $NavResDir ("{0}.res" -f $Tiles[0])
        } else { "" }
        if ($exit -ne 0 -or $missing.Count -gt 0) {
            $status = "FAIL"
            Write-Warning ("FAILED {0} (exit={1} missing_res={2})" -f $Name, $exit, ($missing -join ","))
        }
        else {
            $secs = "{0:n1}" -f $sw.Elapsed.TotalSeconds
            $bytes = 0L
            foreach ($t in $Tiles) {
                $bytes += (Get-Item -LiteralPath (Join-Path $NavResDir ("{0}.res" -f $t))).Length
            }
            $kb = "{0:n0}" -f ($bytes / 1KB)
            Write-Host ("OK {0} ({1}s, {2} KB .res x{3})" -f $Name, $secs, $kb, $Tiles.Count)
        }
    }

    $tileList = ($Tiles -join "|")
    $secStr = "{0:n2}" -f $sw.Elapsed.TotalSeconds
    $line = "{0},{1},{2},{3},{4},{5},{6},{7},{8}" -f `
        $Name, $Kind, $Family, $Occ, $tileList, $artifact, $status, $secStr, $ParamProfile
    $resultPath = Join-Path $ResultsDir ("{0}.csv" -f $Name)
    Set-Content -Path $resultPath -Value $line -Encoding utf8

    return [pscustomobject]@{ Name = $Name; Status = $status; Seconds = $sw.Elapsed.TotalSeconds }
}

$swAll = [System.Diagnostics.Stopwatch]::StartNew()

$sessionState = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault()
$pool = [RunspaceFactory]::CreateRunspacePool(1, $Jobs, $sessionState, $Host)
$pool.Open()

$workers = New-Object System.Collections.Generic.List[object]
# Hashtable copy for runspace marshaling (ordered -> plain).
$baselineForJobs = @{}
foreach ($k in $baselineEnv.Keys) { $baselineForJobs[[string]$k] = [string]$baselineEnv[$k] }

function Resolve-CheckpointForGroup {
    param(
        [string]$GroupName,
        [string[]]$Tiles,
        [hashtable]$Profiles,
        [hashtable]$Overrides
    )
    $profile = "cc_baseline_v1"
    $ov = @{}
    if ($Profiles.ContainsKey($GroupName) -and $Profiles[$GroupName]) {
        $profile = [string]$Profiles[$GroupName]
    }
    if ($Overrides.ContainsKey($GroupName) -and $null -ne $Overrides[$GroupName]) {
        $ov = $Overrides[$GroupName]
    }
    elseif ($Tiles) {
        foreach ($t in $Tiles) {
            $tk = [string]$t
            if ($Overrides.ContainsKey($tk) -and $null -ne $Overrides[$tk] -and $Overrides[$tk].Count -gt 0) {
                $ov = $Overrides[$tk]
                if ($Profiles.ContainsKey($tk) -and $Profiles[$tk]) {
                    $profile = [string]$Profiles[$tk]
                }
                break
            }
        }
    }
    return @{ Profile = $profile; Overrides = $ov }
}

for ($i = 0; $i -lt $total; $i++) {
    $g = $groups[$i]
    $gName = [string]$g.name
    $tiles = [string[]]@($g.tiles)
    $resolved = Resolve-CheckpointForGroup -GroupName $gName -Tiles $tiles `
        -Profiles $checkpointProfiles -Overrides $checkpointOverrides
    $profile = [string]$resolved.Profile
    $overrides = $resolved.Overrides
    $ps = [PowerShell]::Create()
    $ps.RunspacePool = $pool
    [void]$ps.AddScript($bakeScript).AddParameters(@{
            Godot         = $godot
            RepoRoot      = $RepoRoot
            Out           = $Out
            LogsDir       = $logsDir
            ResultsDir    = $resultsDir
            Index         = ($i + 1)
            Total         = $total
            Name          = $gName
            Kind          = [string]$g.kind
            Family        = [string]$g.family
            Occ           = $g.occ
            Tiles         = $tiles
            WriteRes      = [bool]$WriteRes
            ExportGlb     = [bool]$ExportGlb
            NavResDir     = $navResDir
            EnvOverrides  = $overrides
            ParamProfile  = $profile
            BaselineEnv   = $baselineForJobs
        })
    $handle = $ps.BeginInvoke()
    $workers.Add([pscustomobject]@{ PS = $ps; Handle = $handle; Name = $gName }) | Out-Null
}

foreach ($w in $workers) {
    try {
        $null = $w.PS.EndInvoke($w.Handle)
        if ($w.PS.HadErrors) {
            foreach ($err in $w.PS.Streams.Error) {
                Write-Warning ("{0}: {1}" -f $w.Name, $err)
            }
        }
    }
    catch {
        Write-Warning ("{0}: {1}" -f $w.Name, $_)
    }
    finally {
        $w.PS.Dispose()
    }
}

$pool.Close()
$pool.Dispose()
$swAll.Stop()

$ok = 0
$fail = 0
foreach ($g in $groups) {
    $name = [string]$g.name
    $resultPath = Join-Path $resultsDir ("{0}.csv" -f $name)
    if (Test-Path -LiteralPath $resultPath) {
        $line = (Get-Content -Raw -Path $resultPath).TrimEnd()
        Add-Content -Path $manifestPath -Value $line -Encoding utf8
        if ($line -match ",FAIL,") { $fail++ } else { $ok++ }
    }
    else {
        $tileList = (@($g.tiles) -join "|")
        $artifact = if ($ExportGlb) {
            Join-Path $Out "$name.glb"
        } else {
            $first = @($g.tiles)[0]
            Join-Path $navResDir ("{0}.res" -f $first)
        }
        $profile = "cc_baseline_v1"
        if ($checkpointProfiles.ContainsKey($name) -and $checkpointProfiles[$name]) {
            $profile = [string]$checkpointProfiles[$name]
        }
        "{0},{1},{2},{3},{4},{5},FAIL,0,{6}" -f $name, $g.kind, $g.family, $g.occ, $tileList, $artifact, $profile |
            Add-Content -Path $manifestPath -Encoding utf8
        $fail++
    }
}

$logPath = Join-Path $Out "bake.log"
if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }
Get-ChildItem -Path $logsDir -Filter "*.log" | Sort-Object Name | ForEach-Object {
    "===== $($_.BaseName) =====" | Add-Content -Path $logPath -Encoding utf8
    Get-Content -Raw -Path $_.FullName | Add-Content -Path $logPath -Encoding utf8
}

if ($ColorizeRegions) {
    $colorPy = Join-Path $PSScriptRoot "colorize_nav_regions.py"
    $regionsDir = Join-Path $Out "regions"
    New-Item -ItemType Directory -Force -Path $regionsDir | Out-Null
    $colorOk = 0
    $colorFail = 0
    Write-Host ""
    Write-Host "Colorizing connected regions -> $regionsDir"
    foreach ($g in $groups) {
        $name = [string]$g.name
        $src = Join-Path $Out "$name.glb"
        $dst = Join-Path $regionsDir "${name}_regions.glb"
        if (-not (Test-Path -LiteralPath $src)) {
            Write-Warning "Skip colorize (missing bake): $name"
            $colorFail++
            continue
        }
        python $colorPy $src $dst
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $dst)) {
            $colorOk++
        }
        else {
            Write-Warning "Colorize FAILED $name"
            $colorFail++
        }
    }
    Write-Host ("Colorize done. ok={0} fail={1} -> {2}" -f $colorOk, $colorFail, $regionsDir)
}

Write-Host ""
Write-Host ("Done. ok={0} fail={1} wall={2:n1}s jobs={3} -> {4}" -f $ok, $fail, $swAll.Elapsed.TotalSeconds, $Jobs, $Out)
if ($fail -gt 0) { exit 1 }
