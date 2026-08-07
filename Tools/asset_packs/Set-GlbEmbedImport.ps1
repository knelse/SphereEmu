<#
.SYNOPSIS
  Tune Godot GLB import for Sphere server visuals:
  - Embed textures in the imported scene (no PNG extract)
  - Disable LODs and shadow meshes (not needed for server view)
.PARAMETER Mode
  Embedded image handling: 3 = Uncompressed (default), 2 = Basis Universal, 1 = Extract.
#>
[CmdletBinding()]
param(
    [ValidateSet(1, 2, 3)]
    [int]$Mode = 3,
    [string]$ModelsRoot = 'Godot/Models',
    [switch]$KeepLods,
    [switch]$KeepShadowMeshes
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$root = Join-Path $repo $ModelsRoot
if (-not (Test-Path -LiteralPath $root)) { throw "Missing $root" }

$generateLods = if ($KeepLods) { 'true' } else { 'false' }
$shadowMeshes = if ($KeepShadowMeshes) { 'true' } else { 'false' }

$files = Get-ChildItem -LiteralPath $root -Recurse -Filter *.glb.import -File
$changed = 0
foreach ($f in $files) {
    $text = Get-Content -LiteralPath $f.FullName -Raw
    $newText = [regex]::Replace($text, 'gltf/embedded_image_handling=\d+', "gltf/embedded_image_handling=$Mode")
    if ($newText -eq $text -and $text -notmatch 'gltf/embedded_image_handling=') {
        if ($text -match '(?m)^\[params\]\r?\n') {
            $newText = $text -replace '(?m)^(\[params\]\r?\n)', "`$1gltf/embedded_image_handling=$Mode`r`n"
        }
    }
    $newText = $newText -replace 'meshes/generate_lods=(true|false)', "meshes/generate_lods=$generateLods"
    $newText = $newText -replace 'meshes/create_shadow_meshes=(true|false)', "meshes/create_shadow_meshes=$shadowMeshes"
    if ($newText -ne $text) {
        Set-Content -LiteralPath $f.FullName -Value $newText -NoNewline -Encoding utf8
        $changed++
    }
}
Write-Host "Updated $changed / $($files.Count) .glb.import"
Write-Host "  embedded_image_handling=$Mode"
Write-Host "  generate_lods=$generateLods  create_shadow_meshes=$shadowMeshes"
Write-Host "Reimport required: godot --headless --path . --import"
