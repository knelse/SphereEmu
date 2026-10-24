# Measure where the visual ground for a raw .lnd tile lands in Godot world space, so the solver's
# raw-tile placement can be compared to what the renderer draws.
#
# Usage (from repo root):
#   .\Tools\motion_frame_probe.ps1

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "GodotPath.ps1")
$godot = (Resolve-GodotExecutable) -replace '_win64\.exe$', '_win64_console.exe'
if (-not (Test-Path $godot)) {
    $godot = Resolve-GodotExecutable
}

$args = @("--headless", "--path", $RepoRoot, "res://Godot/Scenes/motion_frame_probe.tscn")

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
