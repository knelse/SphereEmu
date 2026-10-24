using Godot;
using Godot.Collections;
using FileAccess = Godot.FileAccess;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
/// Loads terrain object placements from the same JSON layout used by <see cref="TerrainObjectsFill"
/// />.
/// </summary>
public static class TerrainObjectPlacementSource
{
    private static readonly Basis SourceWorldToGodotWorldBasis = new (Vector3.Right, Vector3.Down, Vector3.Forward);

    /// <summary>
    /// ObjectData placeholder record. Its model ships a trimesh that would block players
    /// </summary>
    public const string PlaceholderObjectName = "empty";

    /// <summary>
    /// Placements the object-collision bake can build a shape from: named, not the empty placeholder,
    /// not a decorative mesh
    /// </summary>
    public static bool IsBakePlacement (TerrainObjectPlacement placement) =>
        !string.IsNullOrWhiteSpace (placement.ObjectName)
        && placement.ObjectName != PlaceholderObjectName
        && !TerrainObjectsFill.ShouldSkipMeshObstruction (placement.ObjectName);

    /// <summary>
    /// Placements the bake builds and the checks that compare against it expect. One the client
    /// spawns no object for bakes nothing: a marker-only model, or a marker position
    /// (params/sst*.mbd code >= 10000)
    /// </summary>
    public static bool IsCollidablePlacement (TerrainObjectPlacement placement) =>
        IsBakePlacement (placement)
        && !ClientPlacementMarkers.IsMarkerOnly (placement.ObjectName, placement.WorldTransform.Origin);

    /// <summary>
    /// Seasonal placement files, sst_88_halloween.json. The server runs the permanent world, so the
    /// bake and the checks skip these, and the editor includes them on request
    /// </summary>
    public const string SeasonalFileSuffix = "_halloween";

    public static bool IsSeasonalFile (string fileName) =>
        fileName.Contains (SeasonalFileSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Server load. Seasonal files stay out, so the shipped world has no halloween props
    /// </summary>
    public static IReadOnlyList<TerrainObjectPlacement> LoadAll (string objectDataDirectory) =>
        LoadAll (objectDataDirectory, includeSeasonal: false);

    public static IReadOnlyList<TerrainObjectPlacement> LoadAll (string objectDataDirectory, bool includeSeasonal)
    {
        var results = new List<TerrainObjectPlacement> ();
        var dir = objectDataDirectory.TrimEnd ('/') + "/";
        using var da = DirAccess.Open (dir);
        if (da is null)
        {
            GD.PushWarning ($"TerrainObjectPlacementSource: directory not found: {objectDataDirectory}");
            return results;
        }

        da.ListDirBegin ();
        while (true)
        {
            var name = da.GetNext ();
            if (name == string.Empty)
            {
                break;
            }

            if (name is "." or "..")
            {
                continue;
            }

            if (!da.CurrentIsDir () && name.EndsWith (".json", StringComparison.OrdinalIgnoreCase))
            {
                if (includeSeasonal || !IsSeasonalFile (name))
                {
                    AppendRecordsFromJson (dir + name, TerrainObjectWalkCategory.Other, results);
                }

                continue;
            }

            if (da.CurrentIsDir ())
            {
                AppendRecordsFromFolder (dir + name + "/", TerrainObjectWalkCategory.ExtraInstanced, results, includeSeasonal);
            }
        }

        da.ListDirEnd ();
        return results;
    }

    private static void AppendRecordsFromFolder (string folderPath, TerrainObjectWalkCategory category,
        List<TerrainObjectPlacement> results, bool includeSeasonal)
    {
        using var da = DirAccess.Open (folderPath);
        if (da is null)
        {
            return;
        }

        da.ListDirBegin ();
        while (true)
        {
            var name = da.GetNext ();
            if (name == string.Empty)
            {
                break;
            }

            if (name is "." or "..")
            {
                continue;
            }

            if (!da.CurrentIsDir () && name.EndsWith (".json", StringComparison.OrdinalIgnoreCase)
                && (includeSeasonal || !IsSeasonalFile (name)))
            {
                AppendRecordsFromJson (folderPath + name, category, results);
            }
        }

        da.ListDirEnd ();
    }

    private static void AppendRecordsFromJson (string path, TerrainObjectWalkCategory defaultCategory, List<TerrainObjectPlacement> results)
    {
        var jsonText = FileAccess.GetFileAsString (path);
        if (string.IsNullOrEmpty (jsonText))
        {
            return;
        }

        var records = ParseTerrainRecords (jsonText);
        if (records is null)
        {
            return;
        }

        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace (record.ObjectName))
            {
                continue;
            }

            var pos = record.Coordinates?.ToVector3 () ?? Vector3.Zero;
            var rot = record.RotationEuler?.ToEulerRadians () ?? Vector3.Zero;
            var category = defaultCategory == TerrainObjectWalkCategory.ExtraInstanced
                ? TerrainObjectWalkCategory.ExtraInstanced
                : TerrainObjectPlacementSource.ClassifyObjectName (record.ObjectName);

            results.Add (new TerrainObjectPlacement (record.ObjectName, category, BuildPlacementTransform (pos, rot)));
        }
    }

    public static TerrainObjectWalkCategory ClassifyObjectName (string objectName)
    {
        var lower = objectName.ToLowerInvariant ();
        if (lower.Contains ("tree") || lower.Contains ("bush") || lower.Contains ("grass"))
        {
            return TerrainObjectWalkCategory.Plant;
        }

        if (lower.Contains ("rock") || lower.Contains ("stone"))
        {
            return TerrainObjectWalkCategory.Rock;
        }

        return TerrainObjectWalkCategory.Other;
    }

    public static Transform3D BuildPlacementTransform (Vector3 position, Vector3 rotationEuler)
    {
        var basis = Basis.FromEuler (rotationEuler);
        return new Transform3D (basis, position);
    }

    private static List<TerrainObjectRecord>? ParseTerrainRecords (string jsonText)
    {
        var json = new Json ();
        if (json.Parse (jsonText) != Error.Ok)
        {
            return null;
        }

        if (json.Data.VariantType != Variant.Type.Array)
        {
            return null;
        }

        var arr = json.Data.AsGodotArray ();
        var list = new List<TerrainObjectRecord> ();
        foreach (var item in arr)
        {
            if (item.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            var d = item.AsGodotDictionary ();
            string objectName;
            if (d.TryGetValue ("object_name", out var nameVar))
            {
                objectName = nameVar.AsString ().ToLowerInvariant ();
            }
            else if (d.TryGetValue ("name", out var mbdNameVar))
            {
                objectName = mbdNameVar.AsString ().ToLowerInvariant ();
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace (objectName))
            {
                continue;
            }

            var rec = new TerrainObjectRecord { ObjectName = objectName };

            if (d.TryGetValue ("coordinates", out var coordVar) && coordVar.VariantType == Variant.Type.Dictionary)
            {
                var cd = coordVar.AsGodotDictionary ();
                rec.Coordinates = new TerrainCoordinates
                {
                    X = DictGetDouble (cd, "x"),
                    Y = DictGetDouble (cd, "y"),
                    Z = DictGetDouble (cd, "z"),
                };
            }
            else if (d.ContainsKey ("x") || d.ContainsKey ("y") || d.ContainsKey ("z"))
            {
                rec.Coordinates = new TerrainCoordinates
                {
                    X = DictGetDouble (d, "x"),
                    Y = DictGetDouble (d, "y"),
                    Z = DictGetDouble (d, "z"),
                };
            }

            if (d.TryGetValue ("rotation_euler", out var rotVar) && rotVar.VariantType == Variant.Type.Dictionary)
            {
                var rd = rotVar.AsGodotDictionary ();
                rec.RotationEuler = new TerrainRotationEuler
                {
                    Yaw = DictGetDouble (rd, "yaw"),
                    Pitch = DictGetDouble (rd, "pitch"),
                    Roll = DictGetDouble (rd, "roll"),
                };
            }
            else if (d.ContainsKey ("pitch") || d.ContainsKey ("yaw") || d.ContainsKey ("roll"))
            {
                rec.RotationEuler = new TerrainRotationEuler
                {
                    Yaw = DictGetDouble (d, "yaw"),
                    Pitch = DictGetDouble (d, "pitch"),
                    Roll = DictGetDouble (d, "roll"),
                };
            }

            list.Add (rec);
        }

        return list;
    }

    private static double DictGetDouble (Dictionary d, StringName key)
    {
        return d.TryGetValue (key, out var v) ? v.AsDouble () : 0.0;
    }

    private sealed class TerrainObjectRecord
    {
        public string ObjectName { get; set; } = string.Empty;
        public TerrainCoordinates? Coordinates { get; set; }
        public TerrainRotationEuler? RotationEuler { get; set; }
    }

    private sealed class TerrainCoordinates
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public Vector3 ToVector3 ()
        {
            return SourceWorldToGodotWorldBasis * new Vector3 ((float) X, (float) Y, (float) Z);
        }
    }

    private sealed class TerrainRotationEuler
    {
        public double Yaw { get; set; }
        public double Pitch { get; set; }
        public double Roll { get; set; }

        public Vector3 ToEulerRadians ()
        {
            var eulerForGodotBasis = new Vector3 ((float) Pitch, -(float) Yaw, (float) Roll);
            var basisSource = Basis.FromEuler (eulerForGodotBasis);
            var t = SourceWorldToGodotWorldBasis;
            var basisGodot = t * basisSource * t;
            return basisGodot.GetEuler ();
        }
    }
}

public enum TerrainObjectWalkCategory
{
    Plant,
    Rock,
    Other,
    ExtraInstanced,
}

public readonly struct TerrainObjectPlacement
{
    public TerrainObjectPlacement (string objectName, TerrainObjectWalkCategory category, Transform3D worldTransform)
    {
        ObjectName = objectName;
        Category = category;
        WorldTransform = worldTransform;
    }

    public string ObjectName { get; }
    public TerrainObjectWalkCategory Category { get; }
    public Transform3D WorldTransform { get; }
}
