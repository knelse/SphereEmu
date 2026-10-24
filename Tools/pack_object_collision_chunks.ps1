# Bakes object collision: shared Shape3D per model in GodotAssetSource/TerrainBake/ObjectShapes/,
# plus ObjectChunks/index.json keyed by terrain cell. The play-time WorldObjectCollisionStreamer
# loads those, so no model scene is instantiated at runtime.
#
# Requires $env:GODOT_PATH or a Godot install under the GodotPath.ps1 search roots.
#
# Usage (from repo root):
#   .\Tools\pack_object_collision_chunks.ps1
#   .\Tools\pack_object_collision_chunks.ps1 -ObjectData res://Godot/Terrain/ObjectDataJson/

param(
    [string]$TerrainScene = "res://Godot/Scenes/terrain_scene.scn",
    [string]$ObjectData = "res://Godot/Terrain/ObjectDataJson/"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "GodotPath.ps1")
$godot = (Resolve-GodotExecutable) -replace '_win64\.exe$', '_win64_console.exe'
if (-not (Test-Path -LiteralPath $godot)) {
    $godot = Resolve-GodotExecutable
}

$args = @(
    "--headless",
    "--path", $RepoRoot,
    "res://Godot/Scenes/object_collision_pack.tscn",
    "--",
    "--terrain-scene", $TerrainScene,
    "--object-data", $ObjectData
)

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
