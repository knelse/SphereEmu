using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Helpers;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.Fill;

/// <summary>
/// Dump columns: ID, skip, skip, X, Y, Z, Angle, then optional ints. Source (x, y, z) maps to scene
/// (x, -y, -z)
/// </summary>
public static class WorldObjectDumpFillCommon
{
    public static int FloorCoordToInt (double v) =>
        (int) Math.Floor (v);

    public static string BuildPlacementName (string objectTypeName, int id, double x, double y, double z)
    {
        var ix = FloorCoordToInt (x);
        var iy = FloorCoordToInt (y);
        var iz = FloorCoordToInt (z);
        return $"{objectTypeName}_{id:X4}_[{ix}]_[{iy}]_[{iz}]";
    }

    /// <summary>
    /// Bottom-up so a later dump row wins the same position
    /// </summary>
    public static IEnumerable<(int LineNumber, string[] Parts)> EnumerateDataLinesBottomUp (string text)
    {
        var lines = text.Split ('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var lineNumber = i + 1;
            var line = lines[i].TrimEnd ('\r');
            if (string.IsNullOrWhiteSpace (line) || line.TrimStart ().StartsWith ('#'))
            {
                continue;
            }

            var parts = line.Split ((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
            yield return (lineNumber, parts);
        }
    }

    public static bool LooksLikeTypeToken (string token)
    {
        var t = token.Trim ();
        if (t.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < t.Length; i++)
        {
            if (char.IsLetter (t[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesTypeTokenIfPresent (string[] parts, int typeIndex, string expectedTypeValue)
    {
        if (parts.Length <= typeIndex)
        {
            return true;
        }

        var token = parts[typeIndex].Trim ();
        if (!LooksLikeTypeToken (token))
        {
            return true;
        }

        return token.Equals (expectedTypeValue, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tokens containing "e-" are scientific-notation junk, not coordinates
    /// </summary>
    public static bool LooksLikeWeirdCoordToken (string s)
    {
        var t = s.Trim ();
        return t.Contains ("e-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWeirdCoordValue (double v)
    {
        if (double.IsNaN (v) || double.IsInfinity (v))
        {
            return true;
        }

        return Math.Abs (v) > 10000.0;
    }

    public static bool IsAllIntZero (double x, double y, double z) =>
        ((int) x == 0 && (int) y == 0 && (int) z == 0);

    public static bool ShouldSkipWeirdCoords (string xToken, string yToken, string zToken, double x, double y, double z)
    {
        if (LooksLikeWeirdCoordToken (xToken) || LooksLikeWeirdCoordToken (yToken) || LooksLikeWeirdCoordToken (zToken))
        {
            return true;
        }

        if (Math.Abs (y) > 3000.0)
        {
            return true;
        }

        if (IsWeirdCoordValue (x) || IsWeirdCoordValue (y) || IsWeirdCoordValue (z) || IsAllIntZero (x, y, z))
        {
            return true;
        }

        return false;
    }

    public static bool TryLoadPackedScene (string scenePath, string logPrefix, out PackedScene? scene)
    {
        scene = null;
        if (!ResourceLoader.Exists (scenePath))
        {
            GD.PushError ($"{logPrefix}: scene not found: {scenePath}");
            return false;
        }

        scene = ResourceLoader.Load<PackedScene> (scenePath);
        if (scene is null)
        {
            GD.PushError ($"{logPrefix}: could not load: {scenePath}");
            return false;
        }

        return true;
    }

    public static bool TryReadTextFile (string path, string logPrefix, out string text)
    {
        text = string.Empty;

        if (!global::Godot.FileAccess.FileExists (path))
        {
            GD.PushError ($"{logPrefix}: file not found: {path}");
            return false;
        }

        try
        {
            text = File.ReadAllText (path);
        }
        catch (Exception ex)
        {
            GD.PushWarning ($"{logPrefix}: File.ReadAllText failed ({ex.Message}), falling back to Godot FileAccess");
            text = global::Godot.FileAccess.GetFileAsString (path);
        }

        if (string.IsNullOrWhiteSpace (text))
        {
            GD.PushWarning ($"{logPrefix}: empty file: {path}");
            return false;
        }

        return true;
    }

    public static IEnumerable<(int LineNumber, string[] Parts)> EnumerateDataLines (string text)
    {
        var lineNumber = 0;
        using var sr = new StringReader (text);
        while (true)
        {
            var rawLine = sr.ReadLine ();
            if (rawLine is null)
            {
                yield break;
            }

            lineNumber++;
            var line = rawLine.TrimEnd ('\r');
            if (string.IsNullOrWhiteSpace (line) || line.TrimStart ().StartsWith ('#'))
            {
                continue;
            }

            var parts = line.Split ((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
            yield return (lineNumber, parts);
        }
    }

    public static (long Qx, long Qy, long Qz) QuantizeSourcePosition (double x, double y, double z)
    {
        const double scale = 10000.0;
        return (
            (long) Math.Round (x * scale),
            (long) Math.Round (y * scale),
            (long) Math.Round (z * scale));
    }

    public static bool IsPreservedPlacement (Node node) =>
        node is WorldObject { DoNotRebuild: true } or TeleportPointHelper { DoNotRebuild: true };

    /// <summary>
    /// DoNotRebuild nodes stay. Empty grouping nodes left after that are removed too
    /// </summary>
    public static int ClearRebuildableChildren (Node root)
    {
        var removed = 0;
        for (var i = root.GetChildCount () - 1; i >= 0; i--)
        {
            var child = root.GetChild (i);
            if (!GodotObject.IsInstanceValid (child))
            {
                continue;
            }

            if (IsPreservedPlacement (child))
            {
                continue;
            }

            if (child is WorldObject or TeleportPointHelper)
            {
                RemovePlacementNode (child);
                removed++;
                continue;
            }

            if (HasPreservedDescendant (child))
            {
                removed += ClearRebuildableChildren (child);
                if (GodotObject.IsInstanceValid (child) && child.GetChildCount () == 0)
                {
                    RemovePlacementNode (child);
                    removed++;
                }

                continue;
            }

            RemovePlacementNode (child);
            removed++;
        }

        return removed;
    }

    private static bool HasPreservedDescendant (Node node)
    {
        if (IsPreservedPlacement (node))
        {
            return true;
        }

        foreach (var child in node.GetChildren ())
        {
            if (HasPreservedDescendant (child))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemovePlacementNode (Node node)
    {
        if (!GodotObject.IsInstanceValid (node))
        {
            return;
        }

        node.Free ();
    }

    public static Vector3 SourcePositionFromPlacementNode (Node3D placement, Node3D fillRoot)
    {
        var local = fillRoot.ToLocal (placement.GlobalTransform.Origin);
        return new Vector3 (local.X, -local.Y, -local.Z);
    }

    public static (long Qx, long Qy, long Qz) SourcePositionKeyFromPlacementNode (Node3D placement, Node3D fillRoot)
    {
        var source = SourcePositionFromPlacementNode (placement, fillRoot);
        return QuantizeSourcePosition (source.X, source.Y, source.Z);
    }

    /// <summary>
    /// Preserved placements occupy their source position so a rebuild does not stack a second node
    /// there
    /// </summary>
    public static int SeedSeenSourcePositions (Node3D fillRoot, HashSet<(long Qx, long Qy, long Qz)> seenSourcePositions)
    {
        var seeded = 0;
        SeedSeenSourcePositionsRecursive (fillRoot, fillRoot, seenSourcePositions, ref seeded);
        return seeded;
    }

    private static void SeedSeenSourcePositionsRecursive (
        Node node,
        Node3D fillRoot,
        HashSet<(long Qx, long Qy, long Qz)> seenSourcePositions,
        ref int seeded)
    {
        if (IsPreservedPlacement (node) && node is Node3D placement)
        {
            if (seenSourcePositions.Add (SourcePositionKeyFromPlacementNode (placement, fillRoot)))
            {
                seeded++;
            }

            return;
        }

        foreach (var child in node.GetChildren ())
        {
            SeedSeenSourcePositionsRecursive (child, fillRoot, seenSourcePositions, ref seeded);
        }
    }

    /// <summary>
    /// Preserved WorldObject names stay taken so an NPC rebuild does not reuse them
    /// </summary>
    public static int SeedUsedNodeNamesFromPreservedPlacements (Node fillRoot, HashSet<string> usedNames)
    {
        var seeded = 0;
        foreach (var node in fillRoot.FindChildren ("*", recursive: true))
        {
            if (node is not WorldObject { DoNotRebuild: true } wo)
            {
                continue;
            }

            if (usedNames.Add (wo.Name.ToString ()))
            {
                seeded++;
            }
        }

        return seeded;
    }

    /// <summary>
    /// ID column is hex (optional <c>0x</c>).
    /// </summary>
    public static bool TryParseId (string s, out int id) => FileFormatCulture.TryParseHexInt (s, out id);

    public static bool TryParseDouble (string s, out double v) => FileFormatCulture.TryParseDouble (s, out v);

    public static bool TryParseInt (string s, out int v) => FileFormatCulture.TryParseInt (s, out v);

    public static bool TryParseAngle (string s, out int angle) => FileFormatCulture.TryParseAngle (s, out angle);

    public static void SetOwnerIfEditor (Node ownerFallback, Node node)
    {
        if (!Engine.IsEditorHint ())
        {
            return;
        }

        var tree = ownerFallback.GetTree ();
        var root = tree?.EditedSceneRoot;
        node.Owner = root ?? ownerFallback;
    }

    public static bool TryParseCommonPlacementColumns (
        string[] parts,
        out int id,
        out double x,
        out double y,
        out double z,
        out int angleEncoded)
    {
        id = 0;
        x = y = z = 0;
        angleEncoded = 0;

        if (parts.Length < 7)
        {
            return false;
        }

        if (!TryParseId (parts[0].Trim (), out id))
        {
            return false;
        }

        if (!TryParseDouble (parts[3], out x)
            || !TryParseDouble (parts[4], out y)
            || !TryParseDouble (parts[5], out z))
        {
            return false;
        }

        if (!TryParseAngle (parts[6], out angleEncoded))
        {
            return false;
        }

        return true;
    }
}

