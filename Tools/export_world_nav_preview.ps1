param(
    [string]$OutDir = "D:/1",
    [int]$ChunkCount = 16,
    [switch]$MergeOnly
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $Root "Tools\GodotPath.ps1")
$Godot = Resolve-GodotExecutable
$Console = $Godot -replace '_win64\.exe$', '_win64_console.exe'
if (-not (Test-Path -LiteralPath $Console)) { $Console = $Godot }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if (-not $MergeOnly) {
    for ($i = 0; $i -lt $ChunkCount; $i++) {
        $out = Join-Path $OutDir ("world_chunk_{0:D2}.glb" -f $i)
        if (Test-Path -LiteralPath $out) {
            Write-Host "Skip existing chunk $i -> $out"
            continue
        }
        Write-Host "Export chunk $i/$ChunkCount -> $out"
        & $Console --path $Root --headless -s Tools/export_world_nav_glb.gd -- `
            --chunk $i $ChunkCount --out $out
        if ($LASTEXITCODE -ne 0) {
            throw "Chunk $i export failed with exit code $LASTEXITCODE"
        }
    }
}

$merged = Join-Path $OutDir "world_nav_preview.glb"
Write-Host "Merging chunks -> $merged"
python (Join-Path $Root "Tools\merge_world_nav_glb.py") `
    --chunks (Join-Path $OutDir "world_chunk_*.glb") `
    --out $merged
if ($LASTEXITCODE -ne 0) { throw "Merge failed" }
Write-Host "Finished: $merged"
