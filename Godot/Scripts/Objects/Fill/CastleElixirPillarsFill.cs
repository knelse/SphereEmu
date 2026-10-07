using System;
using System.Collections.Generic;
using SphServer.Helpers;
using System.IO;
using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.Fill;

/// <summary>
/// Source (x, y, z) maps to scene (x, -y, -z), same as terrain
/// </summary>
[Tool]
public partial class CastleElixirPillarsFill : Node3D
{
    [Export]
    public string CastleElixirPillarsDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\castle_elixir_pillars.txt";

    [Export]
    public string WorldObjectScenePath { get; set; } = "res://Godot/Scenes/castle_elixir_pillar.tscn";

    [Export]
    public string PillarModelName { get; set; } = "cs_knot";

    [ExportToolButton ("Rebuild castle elixir pillars")]
    public Callable RebuildCastleElixirPillarsButton => Callable.From (RebuildCastleElixirPillars);

    public void RebuildCastleElixirPillars ()
    {
        WorldObjectDumpFillCommon.ClearRebuildableChildren (this);

        if (!ResourceLoader.Exists (WorldObjectScenePath))
        {
            GD.PushError ($"CastleElixirPillarsFill: scene not found: {WorldObjectScenePath}");
            return;
        }

        var scene = ResourceLoader.Load<PackedScene> (WorldObjectScenePath);
        if (scene is null)
        {
            GD.PushError ($"CastleElixirPillarsFill: could not load: {WorldObjectScenePath}");
            return;
        }

        if (!global::Godot.FileAccess.FileExists (CastleElixirPillarsDataFilePath))
        {
            GD.PushError ($"CastleElixirPillarsFill: file not found: {CastleElixirPillarsDataFilePath}");
            return;
        }

        string text;
        try
        {
            // FileAccess.GetFileAsString mishandles some dump encodings; File.ReadAllText accepts
            // absolute Windows paths
            text = File.ReadAllText (CastleElixirPillarsDataFilePath);
        }
        catch (Exception ex)
        {
            GD.PushWarning (
                $"CastleElixirPillarsFill: File.ReadAllText failed ({ex.Message}), falling back to Godot FileAccess");
            text = global::Godot.FileAccess.GetFileAsString (CastleElixirPillarsDataFilePath);
        }

        if (string.IsNullOrWhiteSpace (text))
        {
            GD.PushWarning ($"CastleElixirPillarsFill: empty file: {CastleElixirPillarsDataFilePath}");
            return;
        }

        var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)> ();
        WorldObjectDumpFillCommon.SeedSeenSourcePositions (this, seenSourcePositions);
        var duplicateRowsSkipped = 0;
        var rowsConsidered = 0;
        var rowsParsed = 0;
        var spawned = 0;
        var parseErrors = 0;

        var lineNumber = 0;
        using var sr = new StringReader (text);
        while (true)
        {
            var rawLine = sr.ReadLine ();
            if (rawLine is null)
            {
                break;
            }

            lineNumber++;
            var line = rawLine;
            if (string.IsNullOrWhiteSpace (line) || line.TrimStart ().StartsWith ('#'))
            {
                continue;
            }

            rowsConsidered++;

            // Dumps arrive as TSV or as space-separated copies of the same columns
            var parts = line.Split ((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7)
            {
                parseErrors++;
                GD.PushWarning ($"CastleElixirPillarsFill: line {lineNumber}: expected ≥7 columns, skipping");
                continue;
            }

            if (!TryParseId (parts[0].Trim (), out var id))
            {
                parseErrors++;
                GD.PushWarning ($"CastleElixirPillarsFill: line {lineNumber}: bad ID '{parts[0]}', skipping");
                continue;
            }

            if (!TryParseDouble (parts[3], out var x)
                || !TryParseDouble (parts[4], out var y)
                || !TryParseDouble (parts[5], out var z))
            {
                parseErrors++;
                GD.PushWarning ($"CastleElixirPillarsFill: line {lineNumber}: bad X/Y/Z, skipping");
                continue;
            }

            if (!TryParseAngle (parts[6], out var angleEncoded))
            {
                parseErrors++;
                GD.PushWarning ($"CastleElixirPillarsFill: line {lineNumber}: bad Angle, skipping");
                continue;
            }

            rowsParsed++;

            var posKey = QuantizeSourcePosition (x, y, z);
            if (!seenSourcePositions.Add (posKey))
            {
                duplicateRowsSkipped++;
                continue;
            }

            var localPos = new Vector3 ((float) x, -(float) y, -(float) z);
            CastleTabletLookup.TryGetNearestCastle (this, ToGlobal (localPos), out var castle);

            var instance = scene.Instantiate<CastleElixirPillar> ();
            instance.Name = $"CastleElixirPillar_{(int) castle}_{id:X4}";
            instance.Position = localPos;
            instance.Castle = castle;

            instance.ModelName = PillarModelName;
            instance.Angle = angleEncoded;
            if (id is >= 0 and <= ushort.MaxValue)
            {
                instance.ID = (ushort) id;
            }

            AddChild (instance);
            SetOwnerIfEditor (instance);
            spawned++;
        }

        GD.Print (
            $"CastleElixirPillarsFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, parseErrors={parseErrors}");
    }

    private static (long Qx, long Qy, long Qz) QuantizeSourcePosition (double x, double y, double z)
    {
        const double scale = 10000.0;
        return (
            (long) Math.Round (x * scale),
            (long) Math.Round (y * scale),
            (long) Math.Round (z * scale));
    }

    private static bool TryParseId (string s, out int id) => FileFormatCulture.TryParseHexInt (s, out id);

    private static bool TryParseDouble (string s, out double v) => FileFormatCulture.TryParseDouble (s, out v);

    private static bool TryParseAngle (string s, out int angle) => FileFormatCulture.TryParseAngle (s, out angle);

    private void SetOwnerIfEditor (Node node)
    {
        if (!Engine.IsEditorHint ())
        {
            return;
        }

        var root = GetTree ()?.EditedSceneRoot;
        node.Owner = root ?? this;
    }
}
