# Shared pack definitions for CRC / publish / fetch scripts.
# Dot-source from other Tools/asset_packs scripts.

$script:AssetPackReleaseTag = 'asset-bundles'
$script:AssetPackDefaultRepo = 'knelse/SphereEmu'

# Heavy packs published to the asset-bundles release (CRC-gated).
# Format 'pck' = Godot --export-pack via Preset.
# Format 'zip' = filesystem zip with res://-relative paths (for .gdignore trees Godot cannot pack).
$script:HeavyPacks = @(
    @{
        Id          = 'models'
        Preset      = 'Pack Models'
        Format      = 'pck'
        Extension   = 'pck'
        Roots       = @('Godot/Models')
        IncludeExt  = $null  # all files under roots
    },
    @{
        Id          = 'terrain'
        Preset      = 'Pack Terrain'
        Format      = 'pck'
        Extension   = 'pck'
        Roots       = @('Godot/Terrain')
        IncludeExt  = $null
    },
    @{
        Id          = 'textures'
        Preset      = 'Pack Textures'
        Format      = 'pck'
        Extension   = 'pck'
        Roots       = @('Godot/Textures')
        IncludeExt  = $null
    },
    @{
        # Bake outputs referenced as res://GodotAssetSource/TerrainBake/... in MainServer.
        # Parent .gdignore keeps them out of editor import; zip mounts via LoadResourcePack.
        Id          = 'terrainbake'
        Preset      = $null
        Format      = 'zip'
        Extension   = 'zip'
        Roots       = @('GodotAssetSource/TerrainBake')
        IncludeExt  = $null
    }
)

# Copied into each slim build artifact (not CDN packs).
$script:RuntimeDataItems = @(
    @{ Path = 'appsettings.json'; Dest = 'appsettings.json' },
    @{ Path = 'Config'; Dest = 'Config' },
    @{ Path = 'Sphere.PacketDefinitions'; Dest = 'Sphere.PacketDefinitions' },
    @{ Path = '.generated'; Dest = '.generated' }
)

function Get-RepoRoot {
    param([string]$StartDir = $PSScriptRoot)
    $dir = Resolve-Path (Join-Path $StartDir '..\..')
    return $dir.Path
}

function Get-AssetPackManifestUrl {
    param([string]$BaseUrl = '')
    if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
        $BaseUrl = "https://github.com/$script:AssetPackDefaultRepo/releases/download/$script:AssetPackReleaseTag"
    }
    return "$($BaseUrl.TrimEnd('/'))/manifest.json"
}

function Get-HeavyPackExportOrder {
    # Smallest Godot pcks first; zip bake last (largest, no Godot needed).
    return @('textures', 'terrain', 'models', 'terrainbake')
}

function New-GodotResZipPack {
    <#
    .SYNOPSIS
      Zip repo-relative roots into a Godot LoadResourcePack-compatible archive.
      Entry names are paths relative to the repo root (forward slashes), matching res://.
    #>
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Roots,
        [Parameter(Mandatory)][string]$OutZip
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $repoFull = (Resolve-Path -LiteralPath $RepoRoot).Path.TrimEnd('\', '/')
    $outFull = [System.IO.Path]::GetFullPath($OutZip)
    $outParent = Split-Path -Parent $outFull
    if ($outParent) { New-Item -ItemType Directory -Force -Path $outParent | Out-Null }
    if (Test-Path -LiteralPath $outFull) { Remove-Item -LiteralPath $outFull -Force }

    $zip = [System.IO.Compression.ZipFile]::Open($outFull, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $count = 0
        foreach ($root in $Roots) {
            $absRoot = Join-Path $repoFull $root
            if (-not (Test-Path -LiteralPath $absRoot)) {
                throw "Zip pack root missing: $absRoot"
            }
            Get-ChildItem -LiteralPath $absRoot -Recurse -File -Force |
                Where-Object { $_.Name -ne '.gdignore' } |
                ForEach-Object {
                    $rel = $_.FullName.Substring($repoFull.Length).TrimStart('\', '/').Replace('\', '/')
                    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                        $zip,
                        $_.FullName,
                        $rel,
                        [System.IO.Compression.CompressionLevel]::Fastest
                    )
                    $count++
                }
        }
        Write-Host "Zip pack entries: $count -> $outFull"
    }
    finally {
        $zip.Dispose()
    }
}
