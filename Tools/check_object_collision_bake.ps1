# Asserts the baked object collision sits where the objects are drawn: per sampled cell, the baked
# part centroid against the placement centroid in world. Fails on a non-zero exit code.
#
# Usage (from repo root):
#   .\Tools\check_object_collision_bake.ps1

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
    "res://Godot/Scenes/object_collision_bake_check.tscn"
)

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
