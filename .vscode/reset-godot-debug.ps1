param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'SilentlyContinue'

$vscodeDir = Join-Path $ProjectRoot '.vscode'
$pidFile = Join-Path $vscodeDir 'godot-debug.pid'
$launchFile = Join-Path $vscodeDir 'launch.json'

if (Test-Path $pidFile) {
    $pidText = (Get-Content $pidFile -Raw).Trim()
    if ($pidText -match '^\d+$') {
        Stop-Process -Id ([int]$pidText) -Force
    }

    Remove-Item $pidFile -Force
}

Remove-Item Env:GODOT_MONO_DEBUGGER_AGENT

if (Test-Path $launchFile) {
    $launchJson = Get-Content $launchFile -Raw
    $launchJson = [regex]::Replace(
        $launchJson,
        '("name"\s*:\s*"Godot: Attach"[\s\S]*?"processId"\s*:\s*)\d+',
        '${1}0'
    )
    Set-Content $launchFile $launchJson -Encoding utf8 -NoNewline
}

Write-Host 'Reset Godot debug session.'
