<#
.SYNOPSIS
  Compute CRC32 versions for heavy asset packs (models/terrain/textures).
.EXAMPLE
  .\Tools\asset_packs\Compute-PackCrc.ps1
  .\Tools\asset_packs\Compute-PackCrc.ps1 -OutJson build/pack-crcs.json
#>
[CmdletBinding()]
param(
    [string]$OutJson = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackDefinitions.ps1')
. (Join-Path $PSScriptRoot 'Crc32.ps1')

$repoRoot = Get-RepoRoot
$crcs = Get-AllHeavyPackCrcs -RepoRoot $repoRoot

$manifest = [ordered]@{
    schema = 1
    packs  = [ordered]@{}
}
foreach ($id in $crcs.Keys) {
    $entry = $crcs[$id]
    $manifest.packs[$id] = [ordered]@{
        crc32 = $entry.crc32
        file  = $entry.file
        bytes = 0
    }
    Write-Host ("{0,-10} {1}  ({2})" -f $id, $entry.crc32, ($entry.roots -join ', '))
}

$json = $manifest | ConvertTo-Json -Depth 6
if ($OutJson) {
    $dir = Split-Path -Parent $OutJson
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Set-Content -LiteralPath $OutJson -Value $json -Encoding utf8
    Write-Host "Wrote $OutJson"
} else {
    Write-Output $json
}
