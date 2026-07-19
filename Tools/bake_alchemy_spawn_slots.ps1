# Headless bake of alchemy material spawn slots into MainServer.tscn.
#
# Usage (from repo root):
#   .\Tools\bake_alchemy_spawn_slots.ps1
#   .\Tools\bake_alchemy_spawn_slots.ps1 -Force
#
# By default only spawners without slots (or with bake errors) are baked.

param(
    [switch]$Force,
    [switch]$SkipSceneSave,
    [string]$Scene = "res://Godot/Scenes/MainServer.tscn",
    [string]$NameContains = ""
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
if ($NameContains) { $userArgs += @("--name-contains", $NameContains) }

$args = @(
    "--path", $RepoRoot,
    "--headless",
    "res://Godot/Scenes/alchemy_material_spawn_slot_bake.tscn",
    "--"
) + $userArgs

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
