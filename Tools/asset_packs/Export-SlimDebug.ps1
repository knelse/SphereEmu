<#
.SYNOPSIS
  Build a local Windows Desktop Slim export (same shape as CI) for smoke-testing.
#>
[CmdletBinding()]
param(
    [string]$ExportDir = 'build/windows-debug',
    [string]$Preset = 'Windows Desktop Slim',
    [switch]$FetchPacks,
    [switch]$SkipDotnetBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot

. (Join-Path $repoRoot 'Tools\GodotPath.ps1')
. (Join-Path $PSScriptRoot 'PackDefinitions.ps1')

$godot = Resolve-GodotExecutable
Write-Host "Godot: $godot"

if (-not $SkipDotnetBuild) {
    Write-Host 'dotnet restore/build...'
    dotnet restore SphServer.csproj
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed: $LASTEXITCODE" }
    dotnet build SphServer.csproj --configuration Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $LASTEXITCODE" }
}

$ExportDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ExportDir))
New-Item -ItemType Directory -Force -Path $ExportDir | Out-Null
$outExe = Join-Path $ExportDir 'SphServer.exe'
$log = Join-Path $ExportDir 'export-slim.log'

Write-Host "Exporting preset '$Preset' -> $outExe"
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $godot
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
# Prefer Arguments string for Windows PowerShell 5.1 (.NET Framework) compatibility.
$psi.Arguments = @(
    '--headless',
    '--path',
    "`"$repoRoot`"",
    '--export-debug',
    "`"$Preset`"",
    "`"$outExe`""
) -join ' '

$proc = [System.Diagnostics.Process]::new()
$proc.StartInfo = $psi
[void]$proc.Start()
$stdout = $proc.StandardOutput.ReadToEnd()
$stderr = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()
$combined = @($stdout, $stderr) -join "`n"
Set-Content -LiteralPath $log -Value $combined -Encoding utf8
if ($combined) { Write-Host $combined }
$code = [int]$proc.ExitCode
$proc.Dispose()

Write-Host "export-debug exit=$code"
Get-ChildItem -LiteralPath $ExportDir -Force | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Host

if (-not (Test-Path -LiteralPath $outExe)) {
    throw "Export did not create $outExe (exit=$code). See $log"
}
$size = (Get-Item -LiteralPath $outExe).Length
if ($size -lt 20MB) {
    throw "SphServer.exe is only $size bytes - export likely failed. See $log"
}
Write-Host ("OK SphServer.exe size={0:N1} MB" -f ($size / 1MB))

& (Join-Path $PSScriptRoot 'Copy-RuntimeData.ps1') -ExportDir $ExportDir

# Local build-info so the updater UI has something to show (not a published release).
try {
    $sha = (git -C $repoRoot rev-parse HEAD).Trim()
    $message = (git -C $repoRoot log -1 --pretty=%s).Trim()
    if ([string]::IsNullOrWhiteSpace($message)) { $message = '(local build)' }
    $committedAt = (git -C $repoRoot show -s --format=%cI HEAD).Trim()
    $short = $sha.Substring(0, [Math]::Min(12, $sha.Length))
    $tag = "master-$short"
    $info = [ordered]@{
        sha           = $sha
        shortSha      = $short
        tag           = $tag
        message       = $message
        committedAt   = $committedAt
        builtAt       = [DateTimeOffset]::UtcNow.ToString('o')
        channelTipTag = 'windows-debug-slim'
    }
    $infoPath = Join-Path $ExportDir 'build-info.json'
    ($info | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $infoPath -Encoding utf8
    Write-Host "Wrote $infoPath"

    & (Join-Path $PSScriptRoot 'Write-FileManifest.ps1') `
        -ExportDir $ExportDir `
        -Sha $sha `
        -Tag $tag
}
catch {
    Write-Warning "Could not write build-info/file-manifest: $_"
}

if ($FetchPacks) {
    $packsDir = Join-Path $ExportDir 'packs'
    Write-Host "Fetching asset packs into $packsDir..."
    & (Join-Path $PSScriptRoot 'Fetch-AssetPacks.ps1') -OutDir $packsDir
}

Write-Host ""
Write-Host "Slim build ready: $ExportDir"
Write-Host "Run:  $outExe"
if (-not $FetchPacks) {
    Write-Host "Tip: copy an existing packs\ folder next to the exe, or re-run with -FetchPacks (large download)."
}
