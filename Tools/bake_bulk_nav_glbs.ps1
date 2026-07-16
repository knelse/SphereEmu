# Bakes preview GLBs for every map occurrence of selected tile families.
# 2x2 masters (variant corners 00/01/10/11 on a contiguous block) are combined;
# everything else is 1x1. Plan is generated from Godot/Terrain/map.txt.
#
# Usage (repo root):
#   .\Tools\bake_bulk_nav_glbs.ps1
#   .\Tools\bake_bulk_nav_glbs.ps1 -Out D:/1/my-bulk
#   .\Tools\bake_bulk_nav_glbs.ps1 -PlanOnly
#   .\Tools\bake_bulk_nav_glbs.ps1 -Jobs 8
#   .\Tools\bake_bulk_nav_glbs.ps1 -WriteRes   # also overwrite GeneratedNavMeshes/*.res

param(
    [string]$Out = "",
    [string]$PlanJson = "",
    [string]$Filter = "all",
    [switch]$PlanOnly,
    [switch]$WriteRes,
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

if (-not $Out) {
    $Out = "D:/1/{0} bulk-nav-glbs" -f (Get-Date -Format "yyyy-M-d HH-mm-ss")
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null

$planPy = Join-Path $PSScriptRoot "plan_bulk_nav_bakes.py"
if (-not (Test-Path $planPy)) {
    throw "Missing $planPy - regenerate the bake plan script first."
}
if (-not $PlanJson) { $PlanJson = Join-Path $Out "bulk_nav_bake_plan.json" }
Write-Host "Planning groups from map.txt (filter=$Filter) -> $PlanJson"
python $planPy $PlanJson $Filter | Tee-Object -FilePath (Join-Path $Out "plan.log")

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
if ($StartIndex -gt 0) { $groups = @($groups | Select-Object -Skip $StartIndex) }
if ($Limit -gt 0) { $groups = @($groups | Select-Object -First $Limit) }
Write-Host "Loaded $($groups.Count) bake group(s) from plan."

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
"name,kind,family,occ,tiles,glb,status,seconds" | Set-Content -Path $manifestPath -Encoding utf8

$total = $groups.Count
Write-Host "Godot: $godot"
Write-Host "OUT: $Out"
Write-Host "Groups: $total  Jobs=$Jobs  WriteRes=$WriteRes"
Write-Host "Parallel Godot workers: $Jobs"

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
        [bool]$WriteRes
    )

    $env:NAV_EXPERIMENT_ADDFACES = "2"
    $env:NAV_EXPERIMENT_PRUNE_ISLANDS = "1"
    $env:NAV_EXPERIMENT_AUTHORED_Y = "1"
    $env:NAV_EXPERIMENT_REGION_MIN = "14"
    $env:NAV_EXPERIMENT_SLOPE_DEG = "55"
    $env:NAV_EXPERIMENT_BUILDING_FILL = "1"
    $env:NAV_EXPERIMENT_FILL_INCLUDE = "Town_ph00"

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
    foreach ($a in @("--combined", "--combined-name", $Name, "--out", $Out)) {
        $argList.Add($a) | Out-Null
    }
    if ($WriteRes) { $argList.Add("--write-res") | Out-Null }

    Write-Host ("=== [{0}/{1}] {2} ({3}, {4} tile(s)) ===" -f $Index, $Total, $Name, $Kind, $Tiles.Count)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & $Godot @($argList.ToArray()) 2>&1
    $exit = $LASTEXITCODE
    $sw.Stop()

    $jobLog = Join-Path $LogsDir ("{0}.log" -f $Name)
    ($output | Out-String) | Set-Content -Path $jobLog -Encoding utf8

    $glb = Join-Path $Out "$Name.glb"
    $status = "ok"
    if ($exit -ne 0 -or -not (Test-Path -LiteralPath $glb)) {
        $status = "FAIL"
        Write-Warning ("FAILED {0} (exit={1})" -f $Name, $exit)
    }
    else {
        $secs = "{0:n1}" -f $sw.Elapsed.TotalSeconds
        $kb = "{0:n0}" -f ((Get-Item -LiteralPath $glb).Length / 1KB)
        Write-Host ("OK {0} ({1}s, {2} KB)" -f $Name, $secs, $kb)
    }

    $tileList = ($Tiles -join "|")
    $secStr = "{0:n2}" -f $sw.Elapsed.TotalSeconds
    $line = "{0},{1},{2},{3},{4},{5},{6},{7}" -f `
        $Name, $Kind, $Family, $Occ, $tileList, $glb, $status, $secStr
    $resultPath = Join-Path $ResultsDir ("{0}.csv" -f $Name)
    Set-Content -Path $resultPath -Value $line -Encoding utf8

    return [pscustomobject]@{ Name = $Name; Status = $status; Seconds = $sw.Elapsed.TotalSeconds }
}

$swAll = [System.Diagnostics.Stopwatch]::StartNew()

$sessionState = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault()
$pool = [RunspaceFactory]::CreateRunspacePool(1, $Jobs, $sessionState, $Host)
$pool.Open()

$workers = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $total; $i++) {
    $g = $groups[$i]
    $ps = [PowerShell]::Create()
    $ps.RunspacePool = $pool
    [void]$ps.AddScript($bakeScript).AddParameters(@{
            Godot      = $godot
            RepoRoot   = $RepoRoot
            Out        = $Out
            LogsDir    = $logsDir
            ResultsDir = $resultsDir
            Index      = ($i + 1)
            Total      = $total
            Name       = [string]$g.name
            Kind       = [string]$g.kind
            Family     = [string]$g.family
            Occ        = $g.occ
            Tiles      = [string[]]@($g.tiles)
            WriteRes   = [bool]$WriteRes
        })
    $handle = $ps.BeginInvoke()
    $workers.Add([pscustomobject]@{ PS = $ps; Handle = $handle; Name = [string]$g.name }) | Out-Null
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
        $glb = Join-Path $Out "$name.glb"
        "{0},{1},{2},{3},{4},{5},FAIL,0" -f $name, $g.kind, $g.family, $g.occ, $tileList, $glb |
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
