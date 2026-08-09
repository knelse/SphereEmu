# Open Godot editor on MainServer and auto Load All world chunks.
#
# Usage (from repo root):
#   .\Tools\open_editor_load_all_chunks.ps1
#
# Equivalent:
#   godot --editor --path <repo> res://Godot/Scenes/MainServer.tscn -- --load-all-world-chunks

param(
    [string]$Scene = "res://Godot/Scenes/MainServer.tscn"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "GodotPath.ps1")
$godot = Resolve-GodotExecutable
if (-not $godot -or -not (Test-Path $godot)) {
    throw "Godot executable not found. Set GODOT_PATH or install a mono Godot 4.6+ build."
}

$args = @(
    "--editor",
    "--path", $RepoRoot,
    $Scene,
    "--",
    "--load-all-world-chunks"
)

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
