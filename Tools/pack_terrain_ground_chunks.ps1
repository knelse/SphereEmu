# Pack per-cell ground chunks + shared meshes, strip MeshLibrary from terrain_scene.
# (Full MeshLibrary lives under GodotAssetSource/Terrain/; runtime uses ground chunks.)
#
# Usage (from repo root):
#   .\Tools\pack_terrain_ground_chunks.ps1
#   .\Tools\pack_terrain_ground_chunks.ps1 -NoStrip

param(
    [string]$TerrainScene = "res://Godot/Scenes/terrain_scene.scn",
    [switch]$NoStrip
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "GodotPath.ps1")
$godot = (Resolve-GodotExecutable) -replace '_win64\.exe$', '_win64_console.exe'
if (-not (Test-Path $godot)) {
    $godot = Resolve-GodotExecutable
}

$userArgs = @("--terrain-scene", $TerrainScene)
if ($NoStrip) { $userArgs += "--no-strip" }

$args = @(
    "--headless",
    "--path", $RepoRoot,
    "res://Godot/Scenes/terrain_ground_pack.tscn",
    "--"
) + $userArgs

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
