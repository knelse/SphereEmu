# Convert Model GLBs into Godot/Models/*.scn; keep sources in GodotAssetSource/Models/ (.gdignore).
#
# One-time migrate (GLBs currently under Godot/Models):
#   .\Tools\rebuild_models_scn.ps1 -FromModels
#
# Ongoing rebuild after editing GodotAssetSource/Models GLBs:
#   .\Tools\rebuild_models_scn.ps1
#   .\Tools\rebuild_models_scn.ps1 -Filter Basket1

param(
    [switch]$FromModels,
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

$ModelsDir = Join-Path $Root "Godot\Models"
$AssetSourceRoot = Join-Path $Root "GodotAssetSource"
$ModelSourceDir = Join-Path $AssetSourceRoot "Models"
$Script = "Tools/rebuild_models_scn.gd"
$TmpDir = Join-Path $PSScriptRoot "_tmp"
$LogPath = Join-Path $TmpDir "rebuild_models_scn.log"
New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null

function Get-RelativePath([string]$Full, [string]$Base) {
    $fullPath = [IO.Path]::GetFullPath($Full)
    $basePath = [IO.Path]::GetFullPath($Base).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($basePath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path not under base: $Full"
    }
    return $fullPath.Substring($basePath.Length).Replace('\', '/')
}

function Ensure-ModelSourceRoot {
    New-Item -ItemType Directory -Force -Path $ModelSourceDir | Out-Null
    $gdignore = Join-Path $AssetSourceRoot ".gdignore"
    if (-not (Test-Path -LiteralPath $gdignore)) {
        New-Item -ItemType Directory -Force -Path $AssetSourceRoot | Out-Null
        Set-Content -LiteralPath $gdignore -Value "" -Encoding ascii
    }
}

function Move-GlbsToModelSource {
    Ensure-ModelSourceRoot
    $glbs = Get-ChildItem -LiteralPath $ModelsDir -Recurse -Filter *.glb -File
    Write-Host "Moving $($glbs.Count) GLB(s) Models -> GodotAssetSource/Models..."
    foreach ($glb in $glbs) {
        $rel = Get-RelativePath $glb.FullName $ModelsDir
        $dest = Join-Path $ModelSourceDir ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
        $destDir = Split-Path -Parent $dest
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        if (Test-Path -LiteralPath $dest) {
            Remove-Item -LiteralPath $dest -Force
        }
        Move-Item -LiteralPath $glb.FullName -Destination $dest
        $import = "$($glb.FullName).import"
        if (Test-Path -LiteralPath $import) {
            Remove-Item -LiteralPath $import -Force
        }
    }
    # Orphan imports if any remain
    Get-ChildItem -LiteralPath $ModelsDir -Recurse -Filter *.glb.import -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

function Stage-GlbsFromModelSource {
    if (-not (Test-Path -LiteralPath $ModelSourceDir)) {
        throw "GodotAssetSource/Models not found: $ModelSourceDir"
    }
    $glbs = Get-ChildItem -LiteralPath $ModelSourceDir -Recurse -Filter *.glb -File
    if ($Filter) {
        $filterLower = $Filter.ToLowerInvariant()
        $glbs = @($glbs | Where-Object {
            $_.BaseName.ToLowerInvariant() -eq $filterLower -or
            (Get-RelativePath $_.FullName $ModelSourceDir).ToLowerInvariant().Contains($filterLower)
        })
    }
    if ($glbs.Count -eq 0) {
        throw "No GLBs to stage from GodotAssetSource/Models (filter='$Filter')"
    }
    Write-Host "Staging $($glbs.Count) GLB(s) GodotAssetSource/Models -> Models for import..."
    $staged = @()
    foreach ($glb in $glbs) {
        $rel = Get-RelativePath $glb.FullName $ModelSourceDir
        $dest = Join-Path $ModelsDir ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
        $destDir = Split-Path -Parent $dest
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        Copy-Item -LiteralPath $glb.FullName -Destination $dest -Force
        $staged += $dest
    }
    return $staged
}

function Clear-StagedGlbs {
    Write-Host "Removing staging GLBs + .import from Models..."
    Get-ChildItem -LiteralPath $ModelsDir -Recurse -Filter *.glb -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            $import = "$($_.FullName).import"
            Remove-Item -LiteralPath $_.FullName -Force
            if (Test-Path -LiteralPath $import) { Remove-Item -LiteralPath $import -Force }
        }
}

Write-Host "Models SCN rebuild"
Write-Host "  project: $Root"
Write-Host "  godot:   $Godot"
Write-Host "  mode:    $(if ($FromModels) { 'FromModels (migrate)' } else { 'FromGodotAssetSource/Models' })"
if ($Filter) { Write-Host "  filter:  $Filter" }

$godotUserArgs = @()
if ($Filter) {
    $godotUserArgs += @("--filter", $Filter)
}

try {
    if (-not $FromModels) {
        $null = Stage-GlbsFromModelSource
        Write-Host "Running Godot import for staged GLBs..."
        & $Godot --path $Root --import 2>&1 | Tee-Object -FilePath $LogPath
        if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
            Write-Warning "godot --import exited $LASTEXITCODE (continuing to convert if imports exist)"
        }
    }

    $convertArgs = @("-s", $Script, "--path", $Root, "--") + $godotUserArgs + @("--from-models")
    Write-Host "Converting GLBs to SCN..."
    & $Godot @convertArgs 2>&1 | Tee-Object -FilePath $LogPath -Append
    $convertExit = $LASTEXITCODE
    if ($convertExit -ne 0) {
        throw "SCN conversion failed (exit $convertExit). See $LogPath"
    }

    if ($FromModels) {
        Move-GlbsToModelSource
    } else {
        Clear-StagedGlbs
    }
}
catch {
    if (-not $FromModels) {
        Clear-StagedGlbs
    }
    throw
}

Write-Host "Done. SCN under Godot/Models; GLB sources under GodotAssetSource/Models/."
exit 0
