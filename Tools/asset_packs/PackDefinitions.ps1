# Shared pack definitions for CRC / publish / fetch scripts.
# Dot-source from other Tools/asset_packs scripts.

$script:AssetPackReleaseTag = 'asset-bundles'
$script:AssetPackDefaultRepo = 'knelse/SphereEmu'

# Heavy packs published to the asset-bundles release (CRC-gated).
$script:HeavyPacks = @(
    @{
        Id          = 'models'
        Preset      = 'Pack Models'
        Roots       = @('Godot/Models')
        IncludeExt  = $null  # all files under roots
    },
    @{
        Id          = 'terrain'
        Preset      = 'Pack Terrain'
        Roots       = @('Godot/Terrain')
        IncludeExt  = $null
    },
    @{
        Id          = 'textures'
        Preset      = 'Pack Textures'
        Roots       = @('Godot/Textures')
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
