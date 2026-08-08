<#
.SYNOPSIS
  Write file-manifest.json (sha256 per relative file) for slim-build integrity checks.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExportDir,
    [string]$OutFile = '',
    [string]$Sha = '',
    [string]$Tag = ''
)

$ErrorActionPreference = 'Stop'
$ExportDir = (Resolve-Path -LiteralPath $ExportDir).Path
if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $OutFile = Join-Path $ExportDir 'file-manifest.json'
}

$excludeDirNames = @(
    'packs', 'logs', 'updates'
)
$excludeFileNames = @(
    'appsettings.json',
    'bannedclients.json',
    'export-slim.log',
    'sph.db', 'sph.db-lock',
    'sph-log.db', 'sph-log.db-lock',
    'file-manifest.json'
)
$excludeExtensions = @('.pending-old', '.partial')

function Test-Excluded([System.IO.FileInfo]$file, [string]$root) {
    $rel = $file.FullName.Substring($root.Length).TrimStart('\', '/')
    $parts = $rel.Replace('\', '/').Split('/')
    if ($parts.Length -gt 0 -and $excludeDirNames -contains $parts[0]) { return $true }
    if ($excludeFileNames -contains $file.Name) { return $true }
    foreach ($ext in $excludeExtensions) {
        if ($file.Name.EndsWith($ext, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

$files = [ordered]@{}
Get-ChildItem -LiteralPath $ExportDir -Recurse -File -Force | ForEach-Object {
    if (Test-Excluded $_ $ExportDir) { return }
    $rel = $_.FullName.Substring($ExportDir.Length).TrimStart('\', '/').Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $files[$rel] = [ordered]@{
        sha256 = $hash
        bytes  = $_.Length
    }
}

# Hash the manifest payload without embedding its own hash; clients verify listed files only.
$payload = [ordered]@{
    sha       = $Sha
    tag       = $Tag
    algorithm = 'sha256'
    files     = $files
}

($payload | ConvertTo-Json -Depth 6 -Compress:$false) | Set-Content -LiteralPath $OutFile -Encoding utf8
Write-Host ("Wrote {0} ({1} files)" -f $OutFile, $files.Count)
