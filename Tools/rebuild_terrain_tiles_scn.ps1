# Convert Terrain tile GLBs into Godot/Terrain/Tiles/*.scn; keep sources in GodotAssetSource/Terrain/Tiles/ (.gdignore).
#
# One-time migrate (GLBs currently under Godot/Terrain/Tiles):
#   .\Tools\rebuild_terrain_tiles_scn.ps1 -FromTiles
#
# Ongoing rebuild after editing GodotAssetSource/Terrain tiles:
#   .\Tools\rebuild_terrain_tiles_scn.ps1
#   .\Tools\rebuild_terrain_tiles_scn.ps1 -Filter patch1_00

param(
    [switch]$FromTiles,
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GodotPath.ps1")
$Root = Split-Path -Parent $PSScriptRoot
$Godot = Resolve-GodotExecutable
$GodotConsole = $Godot -replace '_win64\.exe$', '_win64_console.exe'
if (Test-Path -LiteralPath $GodotConsole) {
    $Godot = $GodotConsole
}

$TilesDir = Join-Path $Root "Godot\Terrain\Tiles"
$AssetSourceRoot = Join-Path $Root "GodotAssetSource"
$TerrainSourceTilesDir = Join-Path $AssetSourceRoot "Terrain\Tiles"
$Script = "Tools/rebuild_terrain_tiles_scn.gd"
$TmpDir = Join-Path $PSScriptRoot "_tmp"
$LogPath = Join-Path $TmpDir "rebuild_terrain_tiles_scn.log"
New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null

function Ensure-TerrainSourceRoot {
    New-Item -ItemType Directory -Force -Path $TerrainSourceTilesDir | Out-Null
    $rootIgnore = Join-Path $AssetSourceRoot ".gdignore"
    if (-not (Test-Path -LiteralPath $rootIgnore)) {
        New-Item -ItemType Directory -Force -Path $AssetSourceRoot | Out-Null
        Set-Content -LiteralPath $rootIgnore -Value "" -Encoding ascii
    }
}

function Move-GlbsToTerrainSource {
    Ensure-TerrainSourceRoot
    $glbs = @(Get-ChildItem -LiteralPath $TilesDir -Filter *.glb -File)
    Write-Host "Moving $($glbs.Count) tile GLB(s) Tiles -> GodotAssetSource/Terrain/Tiles..."
    foreach ($glb in $glbs) {
        $dest = Join-Path $TerrainSourceTilesDir $glb.Name
        if (Test-Path -LiteralPath $dest) {
            Remove-Item -LiteralPath $dest -Force
        }
        Move-Item -LiteralPath $glb.FullName -Destination $dest
        $import = "$($glb.FullName).import"
        if (Test-Path -LiteralPath $import) {
            Remove-Item -LiteralPath $import -Force
        }
    }
    Get-ChildItem -LiteralPath $TilesDir -Filter *.glb.import -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

function Stage-GlbsFromTerrainSource {
    if (-not (Test-Path -LiteralPath $TerrainSourceTilesDir)) {
        throw "GodotAssetSource/Terrain/Tiles not found: $TerrainSourceTilesDir"
    }
    $glbs = @(Get-ChildItem -LiteralPath $TerrainSourceTilesDir -Filter *.glb -File)
    if ($Filter) {
        $filterLower = $Filter.ToLowerInvariant()
        $glbs = @($glbs | Where-Object {
            $_.BaseName.ToLowerInvariant() -eq $filterLower -or
            $_.Name.ToLowerInvariant().Contains($filterLower)
        })
    }
    if ($glbs.Count -eq 0) {
        throw "No tile GLBs to stage (filter='$Filter')"
    }
    Write-Host "Staging $($glbs.Count) GLB(s) GodotAssetSource/Terrain/Tiles -> Godot/Terrain/Tiles..."
    foreach ($glb in $glbs) {
        Copy-Item -LiteralPath $glb.FullName -Destination (Join-Path $TilesDir $glb.Name) -Force
    }
}

function Clear-StagedGlbs {
    Write-Host "Removing staging tile GLBs + .import..."
    Get-ChildItem -LiteralPath $TilesDir -Filter *.glb -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            $import = "$($_.FullName).import"
            Remove-Item -LiteralPath $_.FullName -Force
            if (Test-Path -LiteralPath $import) { Remove-Item -LiteralPath $import -Force }
        }
}

Write-Host "Terrain tiles SCN rebuild"
Write-Host "  project: $Root"
Write-Host "  godot:   $Godot"
Write-Host "  mode:    $(if ($FromTiles) { 'FromTiles (migrate)' } else { 'FromGodotAssetSource/Terrain' })"
if ($Filter) { Write-Host "  filter:  $Filter" }

$godotUserArgs = @()
if ($Filter) {
    $godotUserArgs += @("--filter", $Filter)
}

try {
    if (-not $FromTiles) {
        Stage-GlbsFromTerrainSource
        Write-Host "Running Godot import for staged tile GLBs..."
        & $Godot --path $Root --import 2>&1 | Tee-Object -FilePath $LogPath
    }

    $convertArgs = @("-s", $Script, "--path", $Root, "--") + $godotUserArgs + @("--from-tiles")
    Write-Host "Converting tile GLBs to SCN..."
    & $Godot @convertArgs 2>&1 | Tee-Object -FilePath $LogPath -Append
    $convertExit = $LASTEXITCODE
    if ($convertExit -ne 0) {
        throw "SCN conversion failed (exit $convertExit). See $LogPath"
    }

    if ($FromTiles) {
        Move-GlbsToTerrainSource
    } else {
        Clear-StagedGlbs
    }
}
catch {
    if (-not $FromTiles) {
        Clear-StagedGlbs
    }
    throw
}

Write-Host "Done. SCN under Godot/Terrain/Tiles; GLB sources under GodotAssetSource/Terrain/Tiles/."
exit 0
