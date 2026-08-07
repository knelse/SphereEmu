# Regenerates terrain_scene.scn's TerrainObjects subtree (multimesh visuals only)
# and optionally bakes per-tile navigation meshes to TerrainBake/GeneratedNavMeshes/.
# Requires Godot 4.x on PATH, or set $env:GODOT_PATH to the editor executable.
#
# Examples:
#   .\Tools\rebuild_terrain_objects.ps1              # objects only (default)
#   .\Tools\rebuild_terrain_objects.ps1 -WithNav     # also rebake nav meshes

param(
    [switch]$WithNav
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GodotPath.ps1")
$Root = Split-Path -Parent $PSScriptRoot
$Godot = Resolve-GodotExecutable
# GUI subsystem exe drops stdout under redirection; prefer *_console.exe for bake scripts.
$GodotConsole = $Godot -replace '_win64\.exe$', '_win64_console.exe'
if (Test-Path -LiteralPath $GodotConsole) {
    $Godot = $GodotConsole
}
$Script = "Tools/rebuild_terrain_objects_headless.gd"
$TmpDir = Join-Path $PSScriptRoot "_tmp"
$StubList = Join-Path $TmpDir "legacy_mm_paths.txt"
$ScenePath = Join-Path $Root "Godot\Scenes\terrain_scene.scn"

New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null

# Old MultiMesh ExtResource paths must exist as dependency-free stubs before Godot can load the
# scene. Real TerrainBake copies still reference deleted Godot/Models/*.png sidecars.
Write-Host "Scanning scene for legacy MultiMesh ExtResources..."
$bytes = [System.IO.File]::ReadAllBytes($ScenePath)
$text = [System.Text.Encoding]::ASCII.GetString($bytes)
$paths = [regex]::Matches($text, 'res://Godot/Terrain/GeneratedMultiMeshes/[^\x00]+?\.res') |
    ForEach-Object { $_.Value } |
    Sort-Object -Unique
[System.IO.File]::WriteAllLines($StubList, @($paths))
Write-Host "  wrote $($paths.Count) paths -> $StubList"

Write-Host "Terrain objects rebuild (GPU required for MultiMesh transforms)"
Write-Host "  project: $Root"
Write-Host "  godot:   $Godot"
Write-Host "  script:  $Script"
Write-Host "  nav:     $(if ($WithNav) { 'yes' } else { 'no (--objects-only)' })"

$godotArgs = @("-s", $Script, "--path", $Root)
if (-not $WithNav) {
    $godotArgs += @("--", "--objects-only")
}

# Do not use --headless: dummy renderer ignores MultiMesh instance transforms.
& $Godot @godotArgs
exit $LASTEXITCODE
