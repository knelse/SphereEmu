# Converts walk-surface chunk files from legacy v1/v2 to compressed format v3 (no terrain rebake).
# Requires Godot 4.x on PATH, or set $env:GODOT_PATH to the editor executable.
#
# Examples:
#   .\Tools\convert_walk_surface_chunks.ps1
#   .\Tools\convert_walk_surface_chunks.ps1 -Force   # recompress files already in v3

param(
    [switch]$Force,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Godot = if ($env:GODOT_PATH) { $env:GODOT_PATH } else { "godot" }
$Scene = "res://Godot/Scenes/walk_surface_bake.tscn"

$userArgs = @("--convert-chunks")
if ($Help) { $userArgs += "--help" }
if ($Force) { $userArgs += "--force" }

Write-Host "Walk surface chunk format conversion (v1/v2 -> v3)"
Write-Host "  project: $Root"
Write-Host "  godot:   $Godot"
Write-Host "  scene:   $Scene"
Write-Host "  args:    $($userArgs -join ' ')"

$cmdArgs = @("--headless", "--path", $Root, $Scene, "--") + $userArgs

& $Godot @cmdArgs
exit $LASTEXITCODE
