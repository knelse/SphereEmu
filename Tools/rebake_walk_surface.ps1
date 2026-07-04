# Rebakes outdoor walk-surface atlases headlessly (terrain tile heights + object footprints + nav).
# Requires Godot 4.x on PATH, or set $env:GODOT_PATH to the editor executable.
#
# Examples:
#   .\Tools\rebake_walk_surface.ps1
#   .\Tools\rebake_walk_surface.ps1 -Force
#   .\Tools\rebake_walk_surface.ps1 -ObjectsOnly
#   .\Tools\rebake_walk_surface.ps1 -NavOnly

param(
    [switch]$Force,
    [switch]$ObjectsOnly,
    [switch]$NavOnly,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GodotPath.ps1")
$Root = Split-Path -Parent $PSScriptRoot
$Godot = Resolve-GodotExecutable
$Scene = "res://Godot/Scenes/walk_surface_bake.tscn"

$userArgs = @()
if ($Help) { $userArgs += "--help" }
if ($Force) { $userArgs += "--force" }
if ($ObjectsOnly) { $userArgs += "--objects-only" }
if ($NavOnly) { $userArgs += "--nav-only" }

Write-Host "Walk surface headless bake"
Write-Host "  project: $Root"
Write-Host "  godot:   $Godot"
Write-Host "  scene:   $Scene"
if ($userArgs.Count -gt 0) {
    Write-Host "  args:    $($userArgs -join ' ')"
}

$cmdArgs = @("--headless", "--path", $Root, $Scene)
if ($userArgs.Count -gt 0) {
    $cmdArgs += "--"
    $cmdArgs += $userArgs
}

& $Godot @cmdArgs
exit $LASTEXITCODE
