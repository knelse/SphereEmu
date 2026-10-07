using SphServer.Server.Config;
using SphServer.System;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
/// Three syllables from language/_rnms.txt in one signed int32; GetMName joins them with no
/// separator
/// </summary>
public static class MonsterNameCode
{
    public const int PartRadix = 300;

    private static readonly Lock Gate = new ();
    private static string[]? first;
    private static string[]? middle;
    private static string[]? last;

    public static int Encode (int part0, int part1, int part2)
    {
        return -(part0 + part1 * PartRadix + (part2 + 1) * 90000);
    }

    public static int Roll (out string name)
    {
        EnsureLoaded ();
        var firstParts = first!;
        var middleParts = middle!;
        var lastParts = last!;
        var part0 = RollIndex (firstParts);
        var part1 = RollIndex (middleParts);
        var part2 = RollIndex (lastParts);
        name = firstParts[part0] + middleParts[part1] + lastParts[part2];
        return Encode (part0, part1, part2);
    }

    /// <summary>
    /// The // sentinels are real indices, and a hit on them renders as //
    /// </summary>
    private static int RollIndex (string[] list)
    {
        var start = list.Length > 0 && list[0] == "//" ? 1 : 0;
        var end = list.Length;
        if (end - start > 1 && list[end - 1] == "//")
        {
            end--;
        }

        if (start >= end)
        {
            return Random.Shared.Next (list.Length);
        }

        return Random.Shared.Next (start, end);
    }

    public static string Compose (int code)
    {
        EnsureLoaded ();
        var n = -code - 90000;
        var part0 = PositiveModulo (n, PartRadix);
        n /= PartRadix;
        var part1 = PositiveModulo (n, PartRadix);
        n /= PartRadix;
        var part2 = PositiveModulo (n, PartRadix);
        return Syllable (first!, part0) + Syllable (middle!, part1) + Syllable (last!, part2);
    }

    private static int PositiveModulo (int value, int radix)
    {
        var remainder = value % radix;
        return remainder < 0 ? remainder + radix : remainder;
    }

    private static string Syllable (string[] list, int index)
    {
        return (uint) index < (uint) list.Length ? list[index] : "?";
    }

    private static void EnsureLoaded ()
    {
        if (first is not null)
        {
            return;
        }

        lock (Gate)
        {
            if (first is not null)
            {
                return;
            }

            var path = Path.Combine (
                ServerConfig.AppConfig.RepositoryPath,
                "Sphere.GameDataDecode",
                "language",
                "_rnms.txt");
            var text = File.ReadAllText (path, SphEncoding.Win1251);
            Load (text, out var loadedFirst, out var loadedMiddle, out var loadedLast);
            first = loadedFirst;
            middle = loadedMiddle;
            last = loadedLast;
        }
    }

    /// <summary>
    /// A marker line is not a syllable, and tokens run to the first space, including the //
    /// sentinels
    /// </summary>
    private static void Load (string text, out string[] loadedFirst, out string[] loadedMiddle, out string[] loadedLast)
    {
        string[] markers = ["MALE", "FEMALE", "SURNAME", "MNS1", "MNS2", "MNS3"];
        var section = 0;
        List<string>[] parts = [[], [], []];
        var index = text.IndexOf ("FULL", StringComparison.Ordinal);
        var cursor = index < 0 ? text.Length : index;
        while (cursor < text.Length && text[cursor] != '\0')
        {
            var end = cursor;
            while (end < text.Length && text[end] is not '\r' and not '\n' and not '\0')
            {
                end++;
            }

            var line = text[cursor..end];
            if (section == 0)
            {
                if (line.StartsWith ("FULL", StringComparison.Ordinal))
                {
                    section = 1;
                }
            }
            else
            {
                var nextMarker = section - 1;
                if (nextMarker < markers.Length && line.StartsWith (markers[nextMarker], StringComparison.Ordinal))
                {
                    section++;
                }
                else if (section >= 5 && line.Length > 0 && line[0] > 32)
                {
                    var space = line.IndexOf (' ');
                    parts[section - 5].Add (space < 0 ? line : line[..space]);
                }
            }

            if (end >= text.Length || text[end] == '\0')
            {
                break;
            }

            cursor = end + 1;
            if (text[end] == '\r' && cursor < text.Length && text[cursor] == '\n')
            {
                cursor++;
            }
        }

        if (parts[0].Count == 0 || parts[1].Count == 0 || parts[2].Count == 0)
        {
            throw new InvalidDataException ($"Monster syllables missing in name catalogue ({text.Length} chars).");
        }

        loadedFirst = parts[0].ToArray ();
        loadedMiddle = parts[1].ToArray ();
        loadedLast = parts[2].ToArray ();
    }
}
