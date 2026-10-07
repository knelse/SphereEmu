# sessionStart injects code-comment-style for the rest of the session
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
$null = [Console]::In.ReadToEnd()

$skillPath = Join-Path $PSScriptRoot '..\skills\code-comment-style\SKILL.md'
if (-not (Test-Path -LiteralPath $skillPath)) {
    [Console]::Out.WriteLine('{}')
    exit 0
}

$skill = Get-Content -Raw -LiteralPath $skillPath
$context = "Follow code-comment-style for the rest of this session. It stays on until the session ends.`n`n" + $skill
@{ additional_context = $context } | ConvertTo-Json -Compress
exit 0
