$ToolsDir = $PSScriptRoot
$LocalConfig = Join-Path $ToolsDir "godot.local.ps1"
if (Test-Path -LiteralPath $LocalConfig) {
    . $LocalConfig
}

function Find-GodotExecutable {
    $searchRoots = @(
        "D:\Games\Godot",
        "D:\Download\Old\SphEmuPackage\Godot",
        "$env:LOCALAPPDATA\Programs\Godot",
        "$env:ProgramFiles\Godot",
        "${env:ProgramFiles(x86)}\Godot"
    )

    $candidates = @()
    foreach ($root in $searchRoots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        $candidates += Get-ChildItem -Path $root -Filter "Godot*.exe" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notmatch '_console\.exe$' -and $_.Name -match 'mono' }
    }

    if ($candidates.Count -eq 0) {
        return $null
    }

    $preferred = $candidates | Where-Object { $_.Name -match '4\.6' } | Sort-Object Name -Descending | Select-Object -First 1
    if ($preferred) {
        return $preferred.FullName
    }

    return ($candidates | Sort-Object Name -Descending | Select-Object -First 1).FullName
}

function Resolve-GodotExecutable {
    if ($env:GODOT_PATH) {
        if (-not (Test-Path -LiteralPath $env:GODOT_PATH)) {
            throw "GODOT_PATH is set but file not found: $env:GODOT_PATH"
        }

        return (Resolve-Path -LiteralPath $env:GODOT_PATH).Path
    }

    $cmd = Get-Command godot -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $discovered = Find-GodotExecutable
    if ($discovered) {
        return (Resolve-Path -LiteralPath $discovered).Path
    }

    $exampleConfig = Join-Path $ToolsDir "godot.local.ps1.example"
    throw @"
Godot executable not found.

Quick fix (current PowerShell session):
  `$env:GODOT_PATH = 'D:\Games\Godot\Godot_v4.6.1-stable_mono_win64.exe'

Permanent fix:
  [System.Environment]::SetEnvironmentVariable('GODOT_PATH', 'D:\Games\Godot\Godot_v4.6.1-stable_mono_win64.exe', 'User')
  Then restart the terminal.

Or copy and edit local config:
  copy '$exampleConfig' '$LocalConfig'

Note: running GodotPath.ps1 by itself does nothing useful. Use rebake_walk_surface.ps1 instead.
"@
}

if ($MyInvocation.InvocationName -ne '.') {
    Write-Host "Resolve-GodotExecutable is loaded by rebake/convert scripts via dot-sourcing."
    Write-Host "Testing resolution..."
    Write-Host (Resolve-GodotExecutable)
}
