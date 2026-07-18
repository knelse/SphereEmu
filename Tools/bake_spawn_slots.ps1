# Headless bake of outdoor monster spawn slots into MainServer.tscn.
#
# Usage (from repo root):
#   .\Tools\bake_spawn_slots.ps1
#   .\Tools\bake_spawn_slots.ps1 -Force
#   .\Tools\bake_spawn_slots.ps1 -SkipSceneSave
#
# Progress is checkpointed every 100 dirty spawners to:
#   Godot/Terrain/spawn_slot_bake_progress.json
# Re-run after an interrupt to resume. The scene is packed once at the end (unless -SkipSceneSave).

param(
    [switch]$Force,
    [switch]$SkipSceneSave,
    [string]$Scene = "res://Godot/Scenes/MainServer.tscn",
    [string]$Progress = "res://Godot/Terrain/spawn_slot_bake_progress.json"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "GodotPath.ps1")
$godot = (Resolve-GodotExecutable) -replace '_win64\.exe$', '_win64_console.exe'
if (-not (Test-Path $godot)) {
    $godot = Resolve-GodotExecutable
}

if (-not $godot -or -not (Test-Path $godot)) {
    throw "Godot executable not found. Set GODOT_PATH or install a mono Godot 4.6 build."
}

$userArgs = @()
if ($Force) { $userArgs += "--force" }
if ($SkipSceneSave) { $userArgs += "--skip-scene-save" }
if ($Scene) { $userArgs += @("--scene", $Scene) }
if ($Progress) { $userArgs += @("--progress", $Progress) }

$args = @(
    "--path", $RepoRoot,
    "--headless",
    "res://Godot/Scenes/monster_spawn_slot_bake.tscn",
    "--"
) + $userArgs

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
