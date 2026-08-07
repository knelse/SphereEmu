<#
.SYNOPSIS
  Build changed heavy packs and publish to GitHub release tag asset-bundles.
  Intended for CI (requires gh + GODOT on PATH, GITHUB_TOKEN / GH_TOKEN).
#>
[CmdletBinding()]
param(
    [string]$Godot = 'godot',
    [string]$PacksOutDir = 'build/packs',
    [string]$ReleaseTag = 'asset-bundles',
    [switch]$SkipUpload
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackDefinitions.ps1')
. (Join-Path $PSScriptRoot 'Crc32.ps1')

$repoRoot = Get-RepoRoot
Set-Location $repoRoot

New-Item -ItemType Directory -Force -Path $PacksOutDir | Out-Null
$current = Get-AllHeavyPackCrcs -RepoRoot $repoRoot

$remoteManifest = $null
$manifestUrl = Get-AssetPackManifestUrl
try {
    Write-Host "Fetching remote manifest: $manifestUrl"
    $remoteManifest = Invoke-RestMethod -Uri $manifestUrl -Method Get
} catch {
    Write-Host "No remote manifest yet (first publish or network miss)."
}

$changed = @()
foreach ($id in $current.Keys) {
    $entry = $current[$id]
    $remoteCrc = $null
    if ($remoteManifest -and $remoteManifest.packs -and $remoteManifest.packs.$id) {
        $remoteCrc = [string]$remoteManifest.packs.$id.crc32
    }
    if ($remoteCrc -and ($remoteCrc.ToUpperInvariant() -eq $entry.crc32.ToUpperInvariant())) {
        Write-Host "UNCHANGED $id ($($entry.crc32))"
        continue
    }
    Write-Host "CHANGED   $id ($remoteCrc -> $($entry.crc32))"
    $changed += $id
}

foreach ($id in $changed) {
    $entry = $current[$id]
    $packDef = $script:HeavyPacks | Where-Object { $_.Id -eq $id } | Select-Object -First 1
    $outPck = Join-Path $PacksOutDir $entry.file
    Write-Host "Exporting pack preset '$($packDef.Preset)' -> $outPck"
    & $Godot --headless --path $repoRoot --export-pack $packDef.Preset $outPck
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outPck)) {
        throw "export-pack failed for $id (exit=$LASTEXITCODE)"
    }
    $bytes = (Get-Item -LiteralPath $outPck).Length
    $entry.bytes = $bytes
    $current[$id] = $entry
}

# Build full manifest (keep remote file names for unchanged packs).
$manifest = [ordered]@{
    schema = 1
    packs  = [ordered]@{}
}
foreach ($id in $current.Keys) {
    $entry = $current[$id]
    $file = $entry.file
    $bytes = 0
    if ($entry.bytes) { $bytes = [int64]$entry.bytes }
    elseif ($remoteManifest -and $remoteManifest.packs -and $remoteManifest.packs.$id) {
        $file = [string]$remoteManifest.packs.$id.file
        if ($remoteManifest.packs.$id.bytes) { $bytes = [int64]$remoteManifest.packs.$id.bytes }
    }
    $localPath = Join-Path $PacksOutDir $file
    if ((Test-Path -LiteralPath $localPath) -and $bytes -eq 0) {
        $bytes = (Get-Item -LiteralPath $localPath).Length
    }
    $manifest.packs[$id] = [ordered]@{
        crc32 = $entry.crc32
        file  = $file
        bytes = $bytes
    }
}

$manifestPath = Join-Path $PacksOutDir 'manifest.json'
($manifest | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Wrote $manifestPath"

if ($SkipUpload) {
    Write-Host 'SkipUpload set; not publishing release assets.'
    return
}

$existing = gh release view $ReleaseTag 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating release $ReleaseTag"
    gh release create $ReleaseTag --title 'Asset bundles' --notes 'CRC-versioned Godot heavy asset packs (models/terrain/textures).' --latest=false
}

foreach ($id in $changed) {
    $entry = $current[$id]
    $outPck = Join-Path $PacksOutDir $entry.file
    Write-Host "Uploading $($entry.file)"
    gh release upload $ReleaseTag $outPck --clobber

    # Prune older files for this pack id.
    $assetsJson = gh release view $ReleaseTag --json assets | ConvertFrom-Json
    foreach ($asset in $assetsJson.assets) {
        $name = [string]$asset.name
        if ($name -like "$id-*.pck" -and $name -ne $entry.file) {
            Write-Host "Deleting old asset $name"
            gh release delete-asset $ReleaseTag $name --yes
        }
    }
}

Write-Host "Uploading manifest.json"
gh release upload $ReleaseTag $manifestPath --clobber
Write-Host 'Publish complete.'
