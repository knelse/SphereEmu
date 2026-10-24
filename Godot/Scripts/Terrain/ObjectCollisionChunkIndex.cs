using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using SphServer.Godot.Scripts.Util;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Baked object collision: per terrain cell, the authored model shapes that block players. Cell
/// keys match TerrainGroundIndex. Written by ObjectCollisionPackHeadless
/// </summary>
public sealed class ObjectCollisionChunkIndex
{
    private static readonly JsonSerializerOptions JsonOptions = new ()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<(int Gx, int Gz), List<CollisionPart>> cells = new ();

    /// <summary>
    /// GridMap cell size the chunks were keyed with, so stream radii stay in meters
    /// </summary>
    public float TileSize { get; private set; } = 100f;

    public int CellCount => cells.Count;

    public int PartCount { get; private set; }

    public static ObjectCollisionChunkIndex Load (string jsonPath)
    {
        var index = new ObjectCollisionChunkIndex ();
        if (!ResPathIO.TryReadAllText (jsonPath, out var json))
        {
            GD.PushWarning ($"ObjectCollisionChunkIndex: missing index: {jsonPath}");
            return index;
        }

        FileJson? file;
        try
        {
            file = JsonSerializer.Deserialize<FileJson> (json, JsonOptions);
        }
        catch (Exception ex)
        {
            GD.PushError ($"ObjectCollisionChunkIndex: parse failed ({ex.Message}): {jsonPath}");
            return index;
        }

        index.TileSize = file is { TileSize: > 0f } ? file.TileSize : 100f;
        foreach (var cell in file?.Cells ?? [])
        {
            if (cell.Parts is not { Count: > 0 })
            {
                continue;
            }

            foreach (var part in cell.Parts)
            {
                part.ShapePath = TerrainBakePaths.ObjectShapeRes (part.ShapeName);
            }

            index.cells[(cell.X, cell.Z)] = cell.Parts;
            index.PartCount += cell.Parts.Count;
        }

        return index;
    }

    public static void Save (
        string jsonPath,
        float tileSize,
        IReadOnlyDictionary<(int Gx, int Gz), List<CollisionPart>> cells)
    {
        var file = new FileJson { TileSize = tileSize };
        foreach (var (key, list) in cells)
        {
            file.Cells.Add (new CellJson { X = key.Gx, Z = key.Gz, Parts = list });
        }

        var absolute = ResPathIO.IsVirtualPath (jsonPath)
            ? ProjectSettings.GlobalizePath (jsonPath)
            : jsonPath.Replace ('/', Path.DirectorySeparatorChar);
        var directory = Path.GetDirectoryName (absolute);
        if (!string.IsNullOrEmpty (directory))
        {
            Directory.CreateDirectory (directory);
        }

        File.WriteAllText (absolute, JsonSerializer.Serialize (file, JsonOptions));
    }

    public bool TryGetCell (int gx, int gz, out IReadOnlyList<CollisionPart> parts)
    {
        if (cells.TryGetValue ((gx, gz), out var list))
        {
            parts = list;
            return true;
        }

        parts = [];
        return false;
    }

    public IEnumerable<(int Gx, int Gz)> EnumerateCells ()
    {
        foreach (var key in cells.Keys)
        {
            yield return key;
        }
    }

    /// <summary>
    /// One shape instance under a cell node at GridMap.MapToLocal (gx, 0, gz). Basis rows are
    /// row-major, then Origin: 12 floats
    /// </summary>
    public sealed class CollisionPart
    {
        [JsonPropertyName ("n")]
        public string ShapeName { get; set; } = string.Empty;

        [JsonIgnore]
        public string ShapePath { get; set; } = string.Empty;

        [JsonPropertyName ("t")]
        public float[] Transform { get; set; } = [];

        public Transform3D ToTransform ()
        {
            if (Transform.Length < 12)
            {
                return Transform3D.Identity;
            }

            var basis = new Basis (
                new Vector3 (Transform[0], Transform[1], Transform[2]),
                new Vector3 (Transform[3], Transform[4], Transform[5]),
                new Vector3 (Transform[6], Transform[7], Transform[8]));
            return new Transform3D (basis, new Vector3 (Transform[9], Transform[10], Transform[11]));
        }

        public static float[] FromTransform (Transform3D transform)
        {
            var b = transform.Basis;
            return [
                b.X.X, b.X.Y, b.X.Z,
                b.Y.X, b.Y.Y, b.Y.Z,
                b.Z.X, b.Z.Y, b.Z.Z,
                transform.Origin.X, transform.Origin.Y, transform.Origin.Z,
            ];
        }
    }

    private sealed class FileJson
    {
        [JsonPropertyName ("tileSize")]
        public float TileSize { get; set; } = 100f;

        [JsonPropertyName ("cells")]
        public List<CellJson> Cells { get; set; } = [];
    }

    private sealed class CellJson
    {
        [JsonPropertyName ("x")]
        public int X { get; set; }

        [JsonPropertyName ("z")]
        public int Z { get; set; }

        [JsonPropertyName ("parts")]
        public List<CollisionPart> Parts { get; set; } = [];
    }
}
