# Bakes ModelBounds/index.json: the model-space visual AABB for every model in Godot/Models. The
# server builds the player collision box from it, to match the client's model bounds collision.
#
# Usage (from repo root):
#   .\Tools\pack_model_bounds.ps1

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
    "res://Godot/Scenes/model_bounds_pack.tscn"
)

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
