# Prints the object-collision frame from live scene globals and from the baked scene state, and
# fails when they differ. Run after changing TerrainCollisionFrame.
#
# Usage (from repo root):
#   .\Tools\check_object_collision_frame.ps1

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
    "res://Godot/Scenes/object_collision_frame_check.tscn"
)

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
