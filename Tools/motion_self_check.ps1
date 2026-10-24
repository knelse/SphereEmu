# Measure the ported solver on real .lnd/.mdl geometry: support, snap, walk, fall, recovery.
#
# Usage (from repo root):
#   .\Tools\motion_self_check.ps1

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "GodotPath.ps1")
$godot = (Resolve-GodotExecutable) -replace '_win64\.exe$', '_win64_console.exe'
if (-not (Test-Path $godot)) {
    $godot = Resolve-GodotExecutable
}

$args = @("--headless", "--path", $RepoRoot, "res://Godot/Scenes/motion_self_check.tscn")

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
