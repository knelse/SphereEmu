<#
.SYNOPSIS
  Copy runtime_data sidecars into a slim export directory.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExportDir
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackDefinitions.ps1')

$repoRoot = Get-RepoRoot
New-Item -ItemType Directory -Force -Path $ExportDir | Out-Null

foreach ($item in $script:RuntimeDataItems) {
    $src = Join-Path $repoRoot $item.Path
    if (-not (Test-Path -LiteralPath $src)) {
        Write-Warning "runtime_data missing (skip): $($item.Path)"
        continue
    }
    $dest = Join-Path $ExportDir $item.Dest
    $destParent = Split-Path -Parent $dest
    if ($destParent) { New-Item -ItemType Directory -Force -Path $destParent | Out-Null }

    if ((Get-Item -LiteralPath $src) -is [System.IO.DirectoryInfo]) {
        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
        Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force
    } else {
        Copy-Item -LiteralPath $src -Destination $dest -Force
    }
    Write-Host "Copied $($item.Path) -> $dest"
}
