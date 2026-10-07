using System;
using Godot;
using SphServer.Godot.Scripts.Navigation;

namespace SphServer.Godot.Scripts.World;

/// <summary>
/// 100 m XZ, same size as TerrainNavMeshRuntime.TileSizeWorld. Godot world XZ (GlobalPosition), not
/// nav-local
/// </summary>
public static class WorldTileKeys
{
    public const float TileSizeWorld = TerrainNavMeshRuntime.TileSizeWorld;

    public static (int TileX, int TileZ) FromWorld (Vector3 worldPosition)
    {
        return (
            (int) Math.Floor (worldPosition.X / TileSizeWorld),
            (int) Math.Floor (worldPosition.Z / TileSizeWorld));
    }

    public static string FormatKey (int tileX, int tileZ) => $"{tileX}_{tileZ}";

    public static string FormatKey (Vector3 worldPosition)
    {
        var (tileX, tileZ) = FromWorld (worldPosition);
        return FormatKey (tileX, tileZ);
    }

    public static bool TryParseKey (string key, out int tileX, out int tileZ)
    {
        tileX = 0;
        tileZ = 0;
        if (string.IsNullOrEmpty (key))
        {
            return false;
        }

        var underscore = key.IndexOf ('_');
        if (underscore <= 0 || underscore >= key.Length - 1)
        {
            return false;
        }

        return int.TryParse (key.AsSpan (0, underscore), out tileX)
            && int.TryParse (key.AsSpan (underscore + 1), out tileZ);
    }

    public static Vector3 TileCenterWorld (int tileX, int tileZ)
    {
        return new Vector3 (
            (tileX + 0.5f) * TileSizeWorld,
            0f,
            (tileZ + 0.5f) * TileSizeWorld);
    }
}
