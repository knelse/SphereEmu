# Split MainServer placement nodes into Godot/World/Chunks + world_content_index.bin.
#
# Usage (from repo root):
#   .\Tools\split_world_chunks.ps1
#   .\Tools\split_world_chunks.ps1 -NoSaveMain
#   .\Tools\split_world_chunks.ps1 -KeepSlotsInChunks

param(
    [string]$Scene = "res://Godot/Scenes/MainServer.tscn",
    [switch]$KeepSlotsInChunks,
    [switch]$NoSaveMain
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "GodotPath.ps1")
$godot = (Resolve-GodotExecutable) -replace '_win64\.exe$', '_win64_console.exe'
if (-not (Test-Path $godot)) {
    $godot = Resolve-GodotExecutable
}

if (-not $godot -or -not (Test-Path $godot)) {
    throw "Godot executable not found. Set GODOT_PATH or install a mono Godot 4.6 build."
}

$userArgs = @("--scene", $Scene)
if ($KeepSlotsInChunks) { $userArgs += "--keep-slots-in-chunks" }
if ($NoSaveMain) { $userArgs += "--no-save-main" }

$args = @(
    "--headless",
    "--path", $RepoRoot,
    "res://Godot/Scenes/world_chunk_split.tscn",
    "--"
) + $userArgs

Write-Host "Running: $godot $($args -join ' ')"
& $godot @args
exit $LASTEXITCODE
