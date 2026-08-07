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
    [switch]$SkipUpload,
    [switch]$SkipImport
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackDefinitions.ps1')
. (Join-Path $PSScriptRoot 'Crc32.ps1')

function Resolve-GodotExe {
    param([Parameter(Mandatory)][string]$Godot)
    if (Test-Path -LiteralPath $Godot) {
        return (Resolve-Path -LiteralPath $Godot).Path
    }
    $cmd = Get-Command $Godot -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        throw "Godot executable not found: '$Godot' (set -Godot to a full path)."
    }
    # Prefer Path on application; Source may be a .ps1 shim.
    if ($cmd.CommandType -eq 'Application' -and $cmd.Path) {
        return $cmd.Path
    }
    if ($cmd.Source -and (Test-Path -LiteralPath $cmd.Source) -and $cmd.Source -notlike '*.ps1') {
        return $cmd.Source
    }
    # Shim: try GODOT / GODOT4 env from setup-godot action.
    foreach ($envName in @('GODOT4', 'GODOT', 'GODOT_PATH')) {
        $candidate = [Environment]::GetEnvironmentVariable($envName)
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw "Godot '$Godot' resolved to a non-exe shim and GODOT/GODOT4 env was not set."
}

function Invoke-Godot {
    param(
        [Parameter(Mandatory)][string]$Exe,
        [Parameter(Mandatory)][string[]]$GodotArgs,
        [string]$LogPath = ''
    )

    Write-Host "Godot: $Exe"
    Write-Host "Args:  $($GodotArgs -join ' ')"

    # Do not use Start-Process -ArgumentList with a string[]: PowerShell joins elements with
    # spaces and does not quote, so "Pack Textures" becomes two argv tokens ("Pack", "Textures").
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Exe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    foreach ($a in $GodotArgs) {
        [void]$psi.ArgumentList.Add([string]$a)
    }

    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    try {
        [void]$proc.Start()
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        $proc.WaitForExit()
        $outText = $outTask.GetAwaiter().GetResult()
        $errText = $errTask.GetAwaiter().GetResult()
        if ($LogPath) {
            $combined = @()
            if ($outText) { $combined += $outText }
            if ($errText) { $combined += $errText }
            Set-Content -LiteralPath $LogPath -Value ($combined -join "`n") -Encoding utf8
        }
        if ($outText) { Write-Host $outText }
        if ($errText) { Write-Host $errText }
        return [int]$proc.ExitCode
    }
    finally {
        $proc.Dispose()
    }
}

$repoRoot = Get-RepoRoot
Set-Location $repoRoot
$godotExe = Resolve-GodotExe -Godot $Godot
$PacksOutDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PacksOutDir))
New-Item -ItemType Directory -Force -Path $PacksOutDir | Out-Null

if (-not $SkipImport) {
    Write-Host 'Running Godot --import (populate .godot/imported for clean checkouts)...'
    $importLog = Join-Path $PacksOutDir 'godot-import.log'
    $importCode = Invoke-Godot -Exe $godotExe -GodotArgs @('--headless', '--path', $repoRoot, '--import') -LogPath $importLog
    Write-Host "import exit=$importCode (log: $importLog)"
    # Import can return non-zero with editor plugins; continue if .godot exists.
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.godot'))) {
        throw "Godot --import did not create .godot (exit=$importCode). See $importLog"
    }
}

$current = Get-AllHeavyPackCrcs -RepoRoot $repoRoot

$remoteManifest = $null
$manifestUrl = Get-AssetPackManifestUrl
try {
    Write-Host "Fetching remote manifest: $manifestUrl"
    $remoteManifest = Invoke-RestMethod -Uri $manifestUrl -Method Get
} catch {
    Write-Host "No remote manifest yet (first publish or network miss)."
}

# Smallest packs first so CI validates the pipeline before multi-GB work.
$exportOrder = @('textures', 'terrain', 'models')
$changed = @()
foreach ($id in $exportOrder) {
    if (-not $current.Contains($id)) { continue }
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
    if ($null -eq $packDef) { throw "Unknown pack id '$id'" }

    $outPck = Join-Path $PacksOutDir $entry.file
    $logPath = Join-Path $PacksOutDir "export-$id.log"
    if (Test-Path -LiteralPath $outPck) { Remove-Item -LiteralPath $outPck -Force }

    Write-Host "Exporting pack preset '$($packDef.Preset)' -> $outPck"
    $code = Invoke-Godot -Exe $godotExe -GodotArgs @(
        '--headless',
        '--path', $repoRoot,
        '--export-pack', $packDef.Preset,
        $outPck
    ) -LogPath $logPath

    if ($code -ne 0 -or -not (Test-Path -LiteralPath $outPck)) {
        $tail = ''
        if (Test-Path -LiteralPath $logPath) {
            $tail = (Get-Content -LiteralPath $logPath -Tail 40) -join "`n"
        }
        throw "export-pack failed for $id (exit=$code, exists=$(Test-Path -LiteralPath $outPck)). Log tail:`n$tail"
    }

    $bytes = (Get-Item -LiteralPath $outPck).Length
    Write-Host ("OK {0} size={1:N1} MB" -f $id, ($bytes / 1MB))
    $entry.bytes = $bytes
    $current[$id] = $entry
}

# Build full manifest (keep remote file names for unchanged packs).
$manifest = [ordered]@{
    schema = 1
    packs  = [ordered]@{}
}
foreach ($id in $exportOrder) {
    if (-not $current.Contains($id)) { continue }
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

gh release view $ReleaseTag 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating release $ReleaseTag"
    gh release create $ReleaseTag --title 'Asset bundles' --notes 'CRC-versioned Godot heavy asset packs (models/terrain/textures).' --latest=false
}

foreach ($id in $changed) {
    $entry = $current[$id]
    $outPck = Join-Path $PacksOutDir $entry.file
    Write-Host "Uploading $($entry.file)"
    gh release upload $ReleaseTag $outPck --clobber

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
