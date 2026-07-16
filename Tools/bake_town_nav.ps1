# Bakes all town nav groups as 2x2 combined blocks (never 1x1 cells).
#
# Policy (locked after Town_ph00 / Town4 review):
#   - Town_ph00  → current path (building fill + fabricated roof caps)
#   - All others → baseline carve (no fill)
#
# Each group is baked as one continuous Recast mesh over the four cells, then split into
# four production .res files under Godot/Terrain/GeneratedNavMeshes/. Optional GLB preview.
#
# Usage (from repo root):
#   .\Tools\bake_town_nav.ps1
#   .\Tools\bake_town_nav.ps1 -BakeOnly
#   .\Tools\bake_town_nav.ps1 -Groups Town_ph00,Town4 -Out D:/1/town-preview
#   .\Tools\bake_town_nav.ps1 -GlbOnly -Out D:/1/town-preview

param(
    [string[]]$Groups = @(
        "Town_ph00",
        "Town_rd",
        "Town1_hr",
        "Town2",
        "Town2_hr",
        "Town3",
        "Town4",
        "Town5"
    ),
    [string]$Out = "",
    [switch]$BakeOnly,
    [switch]$GlbOnly
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "GodotPath.ps1")
$godot = (Resolve-GodotExecutable) -replace '_win64\.exe$', '_win64_console.exe'
if (-not (Test-Path $godot)) {
    $godot = Resolve-GodotExecutable
}
if (-not $godot) {
    throw "Godot executable not found. Set GODOT_PATH or install Godot mono."
}

# Standard experiment pack that produced the approved town baselines / Town_ph00 fill path.
$env:NAV_EXPERIMENT_ADDFACES = "2"
$env:NAV_EXPERIMENT_PRUNE_ISLANDS = "1"
$env:NAV_EXPERIMENT_AUTHORED_Y = "1"
$env:NAV_EXPERIMENT_REGION_MIN = "14"
$env:NAV_EXPERIMENT_SLOPE_DEG = "55"
# Building fill on globally; GDScript default allowlist restricts it to Town_ph00 only.
$env:NAV_EXPERIMENT_BUILDING_FILL = "1"
$env:NAV_EXPERIMENT_FILL_INCLUDE = "Town_ph00"

if (-not $BakeOnly -and -not $Out) {
    $Out = "D:/1/{0} town-bake" -f (Get-Date -Format "yyyy-M-d HH-mm-ss")
}
if ($Out) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
}

Write-Host "Godot: $godot"
Write-Host "Groups: $($Groups -join ', ')"
Write-Host "Fill allowlist: Town_ph00 only (baseline for all other towns)"
Write-Host "Mode: $(if ($BakeOnly) { 'bake-only (.res)' } elseif ($GlbOnly) { "GLB only → $Out" } else { ".res + GLB → $Out" })"

$failed = 0
foreach ($g in $Groups) {
    $tiles = @(
        "--tile", "${g}_00_00",
        "--tile", "${g}_01_00",
        "--tile", "${g}_10_00",
        "--tile", "${g}_11_00"
    )
    $args = @("--path", ".", "--headless", "-s", "Tools/bake_and_export_single_nav.gd", "--") + $tiles + @(
        "--combined", "--combined-name", $g
    )
    if ($BakeOnly) {
        $args += "--bake-only"  # implies --write-res
    }
    elseif (-not $GlbOnly) {
        # Full bake: write production .res from the 2x2 mesh AND export preview GLB.
        $args += "--write-res"
    }
    # -GlbOnly: preview GLB only (does not clobber GeneratedNavMeshes/*.res)
    if ($Out -and -not $BakeOnly) {
        $args += @("--out", $Out)
    }

    Write-Host ""
    Write-Host "=== $g (2x2 combined) ==="
    & $godot @args 2>&1 | Select-String -Pattern "Combined:|saved |Exported|ERROR|error|Done \(combined\)"
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Bake failed for $g (exit $LASTEXITCODE)"
        $failed++
    }
}

# Clear experiment env so a subsequent interactive Godot session doesn't inherit them.
$env:NAV_EXPERIMENT_ADDFACES = $null
$env:NAV_EXPERIMENT_PRUNE_ISLANDS = $null
$env:NAV_EXPERIMENT_AUTHORED_Y = $null
$env:NAV_EXPERIMENT_REGION_MIN = $null
$env:NAV_EXPERIMENT_SLOPE_DEG = $null
$env:NAV_EXPERIMENT_BUILDING_FILL = $null
$env:NAV_EXPERIMENT_FILL_INCLUDE = $null

if ($Out) { Write-Host "`nOUT=$Out" }
if ($failed -gt 0) {
    throw "$failed town group(s) failed"
}
Write-Host "Done."
