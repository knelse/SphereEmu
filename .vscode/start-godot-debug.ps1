param(
    [Parameter(Mandatory = $true)]
    [string]$GodotExe,
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'

$vscodeDir = Join-Path $ProjectRoot '.vscode'
$pidFile = Join-Path $vscodeDir 'godot-debug.pid'
$launchFile = Join-Path $vscodeDir 'launch.json'

if (-not (Test-Path $GodotExe)) {
    throw "Godot executable not found: $GodotExe"
}

$env:GODOT_MONO_DEBUGGER_AGENT = 'wait'

$godotProcess = Start-Process `
    -FilePath $GodotExe `
    -ArgumentList @('--path', $ProjectRoot) `
    -WorkingDirectory $ProjectRoot `
    -PassThru

if (-not $godotProcess) {
    throw 'Failed to start Godot.'
}

Start-Sleep -Milliseconds 750

if ($godotProcess.HasExited) {
    throw "Godot exited immediately with code $($godotProcess.ExitCode)."
}

$godotProcess.Id | Set-Content $pidFile -Encoding ascii -NoNewline

if (Test-Path $launchFile) {
    $launchJson = Get-Content $launchFile -Raw
    $launchJson = [regex]::Replace(
        $launchJson,
        '("name"\s*:\s*"Godot: Attach"[\s\S]*?"processId"\s*:\s*)\d+',
        "`${1}$($godotProcess.Id)"
    )
    Set-Content $launchFile $launchJson -Encoding utf8 -NoNewline
}

Write-Host "Started Godot for debug attach (PID $($godotProcess.Id))."
