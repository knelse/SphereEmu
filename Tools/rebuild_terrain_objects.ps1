# Headlessly regenerates terrain_scene.scn's TerrainObjects subtree (multimesh visuals only)
# and bakes per-tile navigation meshes to Godot/Terrain/GeneratedNavMeshes/ (external files,
# not embedded as NavigationRegion3D nodes in the scene).
# Requires Godot 4.x on PATH, or set $env:GODOT_PATH to the editor executable.
#
# Example:
#   .\Tools\rebuild_terrain_objects.ps1

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GodotPath.ps1")
$Root = Split-Path -Parent $PSScriptRoot
$Godot = Resolve-GodotExecutable
$Script = "Tools/rebuild_terrain_objects_headless.gd"

Write-Host "Terrain objects headless rebuild"
Write-Host "  project: $Root"
Write-Host "  godot:   $Godot"
Write-Host "  script:  $Script"

& $Godot --headless -s $Script --path $Root
exit $LASTEXITCODE
