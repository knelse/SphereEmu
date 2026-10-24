using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using SphServer.Godot.Scripts.Util;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Model visual AABB per model name, baked from Godot/Models. The client collides an actor with the
/// model bounds box (SphereRenderModel::recoverBounds), so the server box shape comes from here.
/// Written by ModelBoundsPackHeadless
/// </summary>
public sealed class ModelBoundsIndex
{
    private static readonly JsonSerializerOptions JsonOptions = new ()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Fallback for a name with no entry: a waist-high cylinder-ish box
    /// </summary>
    private static readonly Bounds Fallback = new ()
    {
        Size = new[] { 0.6f, 1.8f, 0.6f },
        Min = new[] { -0.3f, -1.8f, -0.3f },
    };

    private static ModelBoundsIndex? loaded;
    private static readonly object LoadLock = new ();

    private readonly Dictionary<string, Bounds> byName = new (StringComparer.OrdinalIgnoreCase);

    public int Count => byName.Count;

    public static ModelBoundsIndex GetOrLoad ()
    {
        lock (LoadLock)
        {
            loaded ??= Read (TerrainBakePaths.ModelBoundsIndexJson);
            return loaded;
        }
    }

    public static void ClearLoaded ()
    {
        lock (LoadLock)
        {
            loaded = null;
        }
    }

    public Bounds Get (string modelName)
    {
        if (string.IsNullOrWhiteSpace (modelName))
        {
            return Fallback;
        }

        return byName.TryGetValue (modelName, out var bounds) ? bounds : Fallback;
    }

    public static void Save (string jsonPath, IEnumerable<(string Name, Bounds Bounds)> entries)
    {
        var file = new FileJson ();
        foreach (var (name, bounds) in entries)
        {
            file.Models.Add (new ModelJson { Name = name, Size = bounds.Size, Min = bounds.Min });
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

    private static ModelBoundsIndex Read (string jsonPath)
    {
        var index = new ModelBoundsIndex ();
        if (!ResPathIO.TryReadAllText (jsonPath, out var json))
        {
            GD.PushWarning ($"ModelBoundsIndex: missing index: {jsonPath}");
            return index;
        }

        FileJson? file;
        try
        {
            file = JsonSerializer.Deserialize<FileJson> (json, JsonOptions);
        }
        catch (Exception ex)
        {
            GD.PushError ($"ModelBoundsIndex: parse failed ({ex.Message}): {jsonPath}");
            return index;
        }

        foreach (var model in file?.Models ?? [])
        {
            if (string.IsNullOrWhiteSpace (model.Name) || model.Size?.Length != 3 || model.Min?.Length != 3)
            {
                continue;
            }

            index.byName[model.Name] = new Bounds { Size = model.Size, Min = model.Min };
        }

        return index;
    }

    /// <summary>
    /// Model-space visual extents. Size is the box, Min the low corner, anchor at the model origin
    /// </summary>
    public sealed class Bounds
    {
        public float[] Size { get; set; } = [];
        public float[] Min { get; set; } = [];

        public Vector3 SizeVector => new (Size[0], Size[1], Size[2]);

        public Vector3 MinVector => new (Min[0], Min[1], Min[2]);

        /// <summary>
        /// Anchor to feet, positive
        /// </summary>
        public float AboveFeet => -Min[1];

        /// <summary>
        /// Box center relative to the anchor
        /// </summary>
        public float CenterY => Min[1] + Size[1] * 0.5f;
    }

    private sealed class FileJson
    {
        [JsonPropertyName ("models")]
        public List<ModelJson> Models { get; set; } = [];
    }

    private sealed class ModelJson
    {
        [JsonPropertyName ("n")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName ("s")]
        public float[] Size { get; set; } = [];

        [JsonPropertyName ("m")]
        public float[] Min { get; set; } = [];
    }
}
