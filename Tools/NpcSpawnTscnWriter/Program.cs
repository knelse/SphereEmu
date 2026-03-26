using System.Globalization;
using System.Text;

namespace SphServer.Tools.NpcSpawnTscnWriter;

/// <summary>
/// Reads tab-separated NPC spawn rows, removes existing nodes under the NPC parent, then appends
/// <c>npc_interactable.tscn</c> instances to <c>MainServer.tscn</c> (empty <c>Node3D</c> parent created if missing).
/// </summary>
internal static class Program
{
    private const string NpcExtResource = "5_5rg3u";

    private const string DefaultRnmsPath =
        @"d:\SphereDev\SphereSource\SphereEmu\Sphere.GameDataDecode\language\_rnms.txt";

    private const string DefaultNpcInteractableTscnPath =
        @"d:\SphereDev\SphereSource\SphereEmu\Godot\Scenes\npc_interactable.tscn";

    private const string DefaultMainServerTscnPath =
        @"d:\SphereDev\SphereSource\SphereEmu\Godot\Scenes\MainServer.tscn";

    private const string DefaultInputTsvPath =
        @"d:\SphereDev\SphereSource\SphereEmu\Sphere.Game\SpawnData\NPC\npc.txt";

    private static int Main (string[] args)
    {
        var options = CliOptions.Parse (args);
        if (options is null)
        {
            PrintUsage ();
            return 1;
        }

        if (!File.Exists (options.InputTsvPath))
        {
            Console.Error.WriteLine ($"Input file not found: {options.InputTsvPath}");
            return 2;
        }

        if (!File.Exists (options.RnmsPath))
        {
            Console.Error.WriteLine ($"Names file not found: {options.RnmsPath}");
            return 3;
        }

        if (!File.Exists (options.MainServerTscnPath))
        {
            Console.Error.WriteLine ($"Main scene not found: {options.MainServerTscnPath}");
            return 4;
        }

        if (!File.Exists (options.NpcInteractableTscnPath))
        {
            Console.Error.WriteLine ($"npc_interactable scene not found: {options.NpcInteractableTscnPath}");
            return 5;
        }

        Encoding.RegisterProvider (CodePagesEncodingProvider.Instance);
        var rnmsLines = File.ReadAllLines (options.RnmsPath, Encoding.GetEncoding (1251));
        var mainSceneText = File.ReadAllText (options.MainServerTscnPath);

        if (!HasNpcRootNode (mainSceneText, options.ParentNodeName))
        {
            mainSceneText = InsertNpcRootNode (mainSceneText, options.ParentNodeName);
            if (!options.DryRun)
            {
                File.WriteAllText (options.MainServerTscnPath, mainSceneText);
                Console.WriteLine ($"Created empty Node3D \"{options.ParentNodeName}\" under MainServer in {options.MainServerTscnPath}");
            }
            else
            {
                Console.WriteLine ($"Dry run: would create empty Node3D \"{options.ParentNodeName}\" under MainServer.");
            }
        }

        var removedCount = 0;
        mainSceneText = RemoveNodesUnderParent (mainSceneText, options.ParentNodeName, out removedCount);
        if (removedCount > 0)
        {
            Console.WriteLine ($"Removed {removedCount} existing node(s) under \"{options.ParentNodeName}\".");
        }

        var idx = FindSpawnInsertIndex (mainSceneText);
        if (idx < 0)
        {
            Console.Error.WriteLine (
                "Could not find insertion point (expected a line starting with [node name=\"NPC_OLD\"] or [node name=\"DungeonEntrances\").");
            return 6;
        }

        var usedNames = new HashSet<string> (StringComparer.Ordinal);
        CollectExistingNpcNodeNames (mainSceneText, options.ParentNodeName, usedNames);

        var sb = new StringBuilder ();
        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines (options.InputTsvPath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace (rawLine) || rawLine.TrimStart ().StartsWith ("//", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = rawLine.Split ('\t');
            if (parts.Length < 13)
            {
                Console.Error.WriteLine ($"Line {lineNumber}: expected at least 13 tab-separated columns, got {parts.Length}. Skipping.");
                continue;
            }

            try
            {
                var row = NpcSpawnRow.Parse (parts);
                var displayName = ResolveDisplayName (rnmsLines, row.NameId);
                var displayNameLatin = displayName;// RussianTransliteration.ToLatin (displayName);
                var prefixedName = $"NPC_{row.Id.ToString (CultureInfo.InvariantCulture)}_" + displayNameLatin;
                var nodeName = MakeUniqueNodeName (SanitizeNodeName (prefixedName), usedNames);
                var godotY = NegateYForGodot (row.Y);

                sb.AppendLine ();
                sb.Append ("[node name=\"");
                sb.Append (EscapeTscnString (nodeName));
                sb.Append ("\" parent=\"");
                sb.Append (EscapeTscnString (options.ParentNodeName));
                sb.Append ("\" unique_id=");
                sb.Append (Random.Shared.Next (100_000_000, int.MaxValue));
                sb.Append (" instance=ExtResource(\"");
                sb.Append (NpcExtResource);
                sb.AppendLine ("\")]");
                sb.Append ("transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, ");
                sb.Append (FormatFloat (row.X));
                sb.Append (", ");
                sb.Append (FormatFloat (godotY));
                sb.Append (", ");
                sb.Append (FormatFloat (row.Z));
                sb.AppendLine (")");
                sb.Append ("NameID = ");
                sb.Append (row.NameId);
                sb.AppendLine ();
                sb.Append ("ModelName = ");
                sb.AppendLine (FormatTscnStringValue (row.ModelName));
                sb.Append ("IconName = ");
                sb.AppendLine (FormatTscnStringValue (row.IconName));
                sb.Append ("NpcType = ");
                sb.Append (row.NpcType);
                sb.AppendLine ();
                sb.AppendLine ("VendorItemTierMax = 15");
                sb.AppendLine ("VendorLocation = 0");
                sb.Append ("Angle = ");
                sb.Append (row.Angle);
                sb.AppendLine ();
                sb.Append ("ID = ");
                sb.Append (row.Id);
                sb.AppendLine ();
                sb.Append ("ObjectType = ");
                sb.AppendLine (((ushort) row.ObjectType).ToString (CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine ($"Line {lineNumber}: {ex.Message}");
            }
        }

        if (sb.Length == 0)
        {
            Console.WriteLine ("No NPC blocks generated.");
            return 0;
        }

        var updated = mainSceneText.Insert (idx, sb.ToString ());
        if (options.DryRun)
        {
            Console.WriteLine (sb.ToString ());
            Console.WriteLine ("--- Dry run: spawn blocks not written (NPC root may have been written if it was missing). ---");
            return 0;
        }

        File.WriteAllText (options.MainServerTscnPath, updated);
        Console.WriteLine ($"Wrote NPC nodes into {options.MainServerTscnPath}");
        return 0;
    }

    private static void CollectExistingNpcNodeNames (string mainSceneText, string parentName, HashSet<string> usedNames)
    {
        var needle = $"parent=\"{parentName}\"";
        foreach (var line in mainSceneText.Split ('\n'))
        {
            if (!line.Contains (needle, StringComparison.Ordinal) || !line.StartsWith ("[node name=\"", StringComparison.Ordinal))
            {
                continue;
            }

            var start = "[node name=\"".Length;
            var end = line.IndexOf ('"', start);
            if (end > start)
            {
                usedNames.Add (line[start..end]);
            }
        }
    }

    /// <summary>Game Y is negated for Godot (e.g. 159 → -159).</summary>
    private static float NegateYForGodot (float y) => -y;

    /// <summary>Empty <see cref="Node3D"/> under MainServer (<c>parent="."</c>) used as parent for spawned NPCs.</summary>
    private static bool HasNpcRootNode (string sceneText, string nodeName)
    {
        var escaped = EscapeTscnString (nodeName);
        return sceneText.Contains ($"[node name=\"{escaped}\" type=\"Node3D\" parent=\".\"", StringComparison.Ordinal);
    }

    /// <summary>Inserts <c>[node name="…" type="Node3D" parent="."]</c> before <c>NPC_OLD</c> or <c>DungeonEntrances</c>.</summary>
    private static string InsertNpcRootNode (string sceneText, string nodeName)
    {
        var markerNpcOld = "\n[node name=\"NPC_OLD\"";
        var markerDungeon = "\n[node name=\"DungeonEntrances\"";
        var idx = sceneText.IndexOf (markerNpcOld, StringComparison.Ordinal);
        if (idx < 0)
        {
            idx = sceneText.IndexOf (markerDungeon, StringComparison.Ordinal);
        }

        if (idx < 0)
        {
            throw new InvalidOperationException (
                "Cannot insert NPC root: no [node name=\"NPC_OLD\"] or [node name=\"DungeonEntrances\"] anchor found.");
        }

        var uniqueId = Random.Shared.Next (100_000_000, int.MaxValue);
        var escaped = EscapeTscnString (nodeName);
        var block = $"\n[node name=\"{escaped}\" type=\"Node3D\" parent=\".\" unique_id={uniqueId}]\n";
        return sceneText.Insert (idx, block);
    }

    /// <summary>Spawn instances use <c>parent="NPC"</c>; insert blocks before <c>NPC_OLD</c> so they sit under <c>NPC</c>, else before <c>DungeonEntrances</c>.</summary>
    private static int FindSpawnInsertIndex (string sceneText)
    {
        var markerNpcOld = "\n[node name=\"NPC_OLD\"";
        var idx = sceneText.IndexOf (markerNpcOld, StringComparison.Ordinal);
        if (idx >= 0)
        {
            return idx;
        }

        var markerDungeon = "\n[node name=\"DungeonEntrances\"";
        return sceneText.IndexOf (markerDungeon, StringComparison.Ordinal);
    }

    /// <summary>Removes every scene node whose parent chain starts at <paramref name="parentName"/> (direct and nested children).</summary>
    private static string RemoveNodesUnderParent (string sceneText, string parentName, out int removedCount)
    {
        removedCount = 0;
        var lines = sceneText.Replace ("\r\n", "\n").Replace ('\r', '\n').Split ('\n');
        var blocks = ParseTscnNodeBlocks (lines);
        if (blocks.Count == 0)
        {
            return sceneText;
        }

        var toRemove = new HashSet<string> (StringComparer.Ordinal);
        var queue = new Queue<string> ();
        foreach (var b in blocks)
        {
            if (b.ParentName == parentName && toRemove.Add (b.NodeName))
            {
                queue.Enqueue (b.NodeName);
            }
        }

        while (queue.Count > 0)
        {
            var n = queue.Dequeue ();
            foreach (var b in blocks)
            {
                if (b.ParentName == n && toRemove.Add (b.NodeName))
                {
                    queue.Enqueue (b.NodeName);
                }
            }
        }

        if (toRemove.Count == 0)
        {
            return sceneText;
        }

        removedCount = toRemove.Count;
        var toRemoveBlocks = blocks
            .Where (b => toRemove.Contains (b.NodeName))
            .OrderByDescending (b => b.StartLine)
            .ToList ();

        foreach (var b in toRemoveBlocks)
        {
            lines = RemoveLineRange (lines, b.StartLine, b.EndLine);
        }

        return string.Join ("\n", lines);
    }

    private readonly record struct TscnNodeBlock (int StartLine, int EndLine, string NodeName, string? ParentName);

    private static List<TscnNodeBlock> ParseTscnNodeBlocks (string[] lines)
    {
        var list = new List<TscnNodeBlock> ();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!line.StartsWith ("[node name=\"", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryParseNodeHeaderLine (line, out var nodeName, out var parentName))
            {
                continue;
            }

            var end = i + 1;
            while (end < lines.Length && !lines[end].StartsWith ('['))
            {
                end++;
            }

            list.Add (new TscnNodeBlock (i, end, nodeName, parentName));
        }

        return list;
    }

    private static bool TryParseNodeHeaderLine (string line, out string nodeName, out string? parentName)
    {
        nodeName = "";
        parentName = null;
        if (!line.StartsWith ("[node name=\"", StringComparison.Ordinal))
        {
            return false;
        }

        var start = "[node name=\"".Length;
        var end = line.IndexOf ('"', start);
        if (end <= start)
        {
            return false;
        }

        nodeName = line[start..end];
        var pIdx = line.IndexOf ("parent=\"", StringComparison.Ordinal);
        if (pIdx >= 0)
        {
            var ps = pIdx + "parent=\"".Length;
            var pe = line.IndexOf ('"', ps);
            if (pe > ps)
            {
                parentName = line[ps..pe];
            }
        }

        return true;
    }

    private static string[] RemoveLineRange (string[] lines, int startLine, int endLineExclusive)
    {
        if (startLine < 0 || endLineExclusive > lines.Length || startLine >= endLineExclusive)
        {
            return lines;
        }

        var len = lines.Length - (endLineExclusive - startLine);
        var result = new string[len];
        Array.Copy (lines, 0, result, 0, startLine);
        Array.Copy (lines, endLineExclusive, result, startLine, lines.Length - endLineExclusive);
        return result;
    }

    private static string ResolveDisplayName (string[] rnmsLines, int nameId)
    {
        var key = (4000 + nameId).ToString (CultureInfo.InvariantCulture);
        foreach (var line in rnmsLines)
        {
            var trimmed = line.TrimStart ();
            if (trimmed.Length == 0 || trimmed.StartsWith ("//", StringComparison.Ordinal))
            {
                continue;
            }

            var firstSpace = trimmed.IndexOf (' ');
            string prefix;
            string rest;
            if (firstSpace < 0)
            {
                prefix = trimmed;
                rest = string.Empty;
            }
            else
            {
                prefix = trimmed[..firstSpace];
                rest = trimmed[(firstSpace + 1)..].TrimEnd ();
            }

            if (prefix != key)
            {
                continue;
            }

            return string.IsNullOrEmpty (rest) ? $"NPC_{nameId}" : rest;
        }

        return $"NPC_{nameId}";
    }

    private static string SanitizeNodeName (string displayName)
    {
        var s = displayName.Replace (' ', '_');
        var sb = new StringBuilder (s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit (c) || c is '_' or '-' or '.')
            {
                sb.Append (c);
            }
            else
            {
                sb.Append ('_');
            }
        }

        var result = sb.ToString ();
        return string.IsNullOrEmpty (result) ? "NPC" : result;
    }

    private static string MakeUniqueNodeName (string baseName, HashSet<string> usedNames)
    {
        if (usedNames.Add (baseName))
        {
            return baseName;
        }

        for (var i = 2;; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (usedNames.Add (candidate))
            {
                return candidate;
            }
        }
    }

    private static string FormatTscnStringValue (string value)
    {
        if (value == "<null>")
        {
            return "\"<null>\"";
        }

        return $"\"{EscapeTscnString (value)}\"";
    }

    private static string EscapeTscnString (string s) => s.Replace ("\\", "\\\\").Replace ("\"", "\\\"");

    private static string FormatFloat (float f) =>
        f.ToString ("G", CultureInfo.InvariantCulture);

    private static void PrintUsage ()
    {
        Console.WriteLine (
            $"""
            NpcSpawnTscnWriter — append NPC instances to MainServer.tscn

            Usage:
              NpcSpawnTscnWriter [--input <spawn.tsv>] [--rnms <_rnms.txt>] [--main <MainServer.tscn>]
                [--npc-interactable <npc_interactable.tscn>] [--parent <NPC>] [--dry-run] [--help]

            TSV columns (tab-separated, one NPC per line):
              ID (hex; optional 0x), ObjectType (enum name, e.g. NpcTrade), SpawnType (ignored), X, Y, Z, Angle, NameId,
              ModelNameLength, ModelName, IconNameLength, IconName, NpcType

            Y from the file is negated for the Godot transform (game ↔ engine convention).

            Defaults (absolute paths; override with options above):
              --input            {DefaultInputTsvPath}
              --rnms             {DefaultRnmsPath}
              --main             {DefaultMainServerTscnPath}
              --npc-interactable {DefaultNpcInteractableTscnPath}
              --parent           NPC (empty Node3D under MainServer; created if missing)
            """);
    }

    private sealed record NpcSpawnRow
    {
        public required int Id { get; init; }
        public required ObjectType ObjectType { get; init; }
        public required float X { get; init; }
        public required float Y { get; init; }
        public required float Z { get; init; }
        public required int Angle { get; init; }
        public required int NameId { get; init; }
        public required string ModelName { get; init; }
        public required string IconName { get; init; }
        public required int NpcType { get; init; }

        public static NpcSpawnRow Parse (string[] p)
        {
            var id = ParseHexInt32 (p[0]);
            var objectType = Enum.Parse<ObjectType> (p[1].Trim (), ignoreCase: true);
            var x = float.Parse (p[3], CultureInfo.InvariantCulture);
            var y = float.Parse (p[4], CultureInfo.InvariantCulture);
            var z = float.Parse (p[5], CultureInfo.InvariantCulture);
            var angle = (int) float.Parse (p[6], CultureInfo.InvariantCulture);
            var nameId = int.Parse (p[7], CultureInfo.InvariantCulture);
            var modelLen = int.Parse (p[8], CultureInfo.InvariantCulture);
            var modelName = p[9];
            var iconLen = int.Parse (p[10], CultureInfo.InvariantCulture);
            var iconName = p[11];
            var npcType = int.Parse (p[12], CultureInfo.InvariantCulture);

            if (modelName.Length != modelLen && modelLen >= 0)
            {
                Console.WriteLine ($"Warning: ModelName length mismatch (declared {modelLen}, actual {modelName.Length}).");
            }

            var modelNameTrimmed = modelName.Trim ();
            if (!modelNameTrimmed.StartsWith ("npc", StringComparison.OrdinalIgnoreCase))
            {
                modelName = string.Empty;
            }
            else
            {
                modelName = modelNameTrimmed;
            }

            if (iconName.Length != iconLen && iconLen >= 0)
            {
                Console.WriteLine ($"Warning: IconName length mismatch (declared {iconLen}, actual {iconName.Length}).");
            }

            var iconNameTrimmed = iconName.Trim ();
            if (!iconNameTrimmed.StartsWith ("npc_", StringComparison.OrdinalIgnoreCase))
            {
                iconName = string.Empty;
            }
            else
            {
                iconName = iconNameTrimmed;
            }

            if (objectType == ObjectType.NpcBanker)
            {
                iconName = "npc_banker";
            }

            return new NpcSpawnRow
            {
                Id = id,
                ObjectType = objectType,
                X = x,
                Y = y,
                Z = z,
                Angle = angle,
                NameId = nameId,
                ModelName = modelName,
                IconName = iconName,
                NpcType = npcType
            };
        }

        private static int ParseHexInt32 (string s)
        {
            s = s.Trim ();
            if (s.StartsWith ("0x", StringComparison.OrdinalIgnoreCase))
            {
                s = s[2..];
            }

            return int.Parse (s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }

    private sealed class CliOptions
    {
        public required string InputTsvPath { get; init; }
        public required string RnmsPath { get; init; }
        public required string MainServerTscnPath { get; init; }
        public required string NpcInteractableTscnPath { get; init; }
        public string ParentNodeName { get; init; } = "NPC";
        public bool DryRun { get; init; }

        public static CliOptions? Parse (string[] args)
        {
            if (args.Length == 1 && args[0] is "-h" or "--help" or "-?")
            {
                return null;
            }

            var input = DefaultInputTsvPath;
            var rnms = DefaultRnmsPath;
            var main = DefaultMainServerTscnPath;
            var npcInteractable = DefaultNpcInteractableTscnPath;
            var parent = "NPC";
            var dry = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--input" or "-i" when i + 1 < args.Length:
                        input = args[++i];
                        break;
                    case "--rnms" when i + 1 < args.Length:
                        rnms = args[++i];
                        break;
                    case "--main" when i + 1 < args.Length:
                        main = args[++i];
                        break;
                    case "--npc-interactable" when i + 1 < args.Length:
                        npcInteractable = args[++i];
                        break;
                    case "--parent" when i + 1 < args.Length:
                        parent = args[++i];
                        break;
                    case "--dry-run":
                        dry = true;
                        break;
                    default:
                        if (!args[i].StartsWith ('-'))
                        {
                            input = args[i];
                        }

                        break;
                }
            }

            return new CliOptions
            {
                InputTsvPath = Path.GetFullPath (input),
                RnmsPath = Path.GetFullPath (rnms),
                MainServerTscnPath = Path.GetFullPath (main),
                NpcInteractableTscnPath = Path.GetFullPath (npcInteractable),
                ParentNodeName = parent,
                DryRun = dry
            };
        }
    }
}
