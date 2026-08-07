# CRC32 helpers for asset pack versioning.
# Dot-source after PackDefinitions.ps1.

if (-not ('AssetPackCrc32' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public static class AssetPackCrc32
{
    private static readonly uint[] Table = CreateTable();

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1u) != 0u ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    public static uint Compute(byte[] data, int offset, int count)
    {
        uint crc = 0xFFFFFFFFu;
        for (int i = 0; i < count; i++)
            crc = Table[(crc ^ data[offset + i]) & 0xFFu] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    public static uint ComputeFile(string path)
    {
        uint crc = 0xFFFFFFFFu;
        using var fs = File.OpenRead(path);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                crc = Table[(crc ^ buffer[i]) & 0xFFu] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    public static string Hex(uint crc) => crc.ToString("X8");
}
'@
}

function Get-FileCrc32Hex {
    param([Parameter(Mandatory)][string]$Path)
    return [AssetPackCrc32]::Hex([AssetPackCrc32]::ComputeFile($Path))
}

function Get-PackContentCrc32Hex {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Roots
    )

    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($root in $Roots) {
        $absRoot = Join-Path $RepoRoot $root
        if (-not (Test-Path -LiteralPath $absRoot)) {
            continue
        }
        Get-ChildItem -LiteralPath $absRoot -Recurse -File -Force |
            Sort-Object FullName |
            ForEach-Object {
                $rel = $_.FullName.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
                $fileCrc = Get-FileCrc32Hex -Path $_.FullName
                $lines.Add("$rel`t$($_.Length)`t$fileCrc")
            }
    }

    $canonical = [string]::Join("`n", $lines)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($canonical)
    return [AssetPackCrc32]::Hex([AssetPackCrc32]::Compute($bytes, 0, $bytes.Length))
}

function Get-AllHeavyPackCrcs {
    param([string]$RepoRoot = (Get-RepoRoot))
    $result = [ordered]@{}
    foreach ($pack in $script:HeavyPacks) {
        $crc = Get-PackContentCrc32Hex -RepoRoot $RepoRoot -Roots $pack.Roots
        $result[$pack.Id] = @{
            crc32 = $crc
            file  = "$($pack.Id)-$crc.pck"
            roots = $pack.Roots
            preset = $pack.Preset
        }
    }
    return $result
}
