<#
.SYNOPSIS
  Download heavy asset packs from the asset-bundles release into ./packs next to a build (or repo).
#>
[CmdletBinding()]
param(
    [string]$OutDir = 'packs',
    [string]$BaseUrl = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackDefinitions.ps1')
. (Join-Path $PSScriptRoot 'Crc32.ps1')

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    $BaseUrl = "https://github.com/$script:AssetPackDefaultRepo/releases/download/$script:AssetPackReleaseTag"
}
$BaseUrl = $BaseUrl.TrimEnd('/')
$manifestUrl = "$BaseUrl/manifest.json"

Write-Host "Fetching $manifestUrl"
$manifest = Invoke-RestMethod -Uri $manifestUrl -Method Get
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$state = [ordered]@{ packs = [ordered]@{} }

foreach ($id in @('models', 'terrain', 'textures')) {
    $info = $manifest.packs.$id
    if (-not $info) { throw "manifest missing pack '$id'" }
    $file = [string]$info.file
    $crc = [string]$info.crc32
    $dest = Join-Path $OutDir $file
    $need = $Force -or -not (Test-Path -LiteralPath $dest)
    if (-not $need -and (Test-Path -LiteralPath $dest)) {
        $localCrc = Get-FileCrc32Hex -Path $dest
        # File CRC of the .pck itself is not the content CRC; keep by name match.
        if ($dest -like "*-$crc.pck" -or [IO.Path]::GetFileName($dest) -eq $file) {
            Write-Host "Have $file"
            $need = $false
        }
    }
    if ($need -or -not (Test-Path -LiteralPath $dest)) {
        $url = "$BaseUrl/$file"
        Write-Host "Downloading $url"
        Invoke-WebRequest -Uri $url -OutFile $dest
    }
    $state.packs[$id] = [ordered]@{ crc32 = $crc; file = $file }
}

$statePath = Join-Path $OutDir 'state.json'
($state | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $statePath -Encoding utf8
Write-Host "Wrote $statePath"
