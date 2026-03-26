using System.Text;

namespace SphServer.Tools.NpcSpawnTscnWriter;

/// <summary>
/// Russian Cyrillic → Latin for Godot node names (BGN/PCGN–style, ASCII).
/// </summary>
internal static class RussianTransliteration
{
    public static string ToLatin (string s)
    {
        if (string.IsNullOrEmpty (s))
        {
            return s;
        }

        var sb = new StringBuilder (s.Length * 2);
        foreach (var c in s)
        {
            if (Map.TryGetValue (c, out var latin))
            {
                sb.Append (latin);
            }
            else if (c is >= '\u0400' and <= '\u04FF')
            {
                sb.Append ('_');
            }
            else
            {
                sb.Append (c);
            }
        }

        return sb.ToString ();
    }

    private static readonly Dictionary<char, string> Map = new ()
    {
        ['А'] = "A", ['а'] = "a",
        ['Б'] = "B", ['б'] = "b",
        ['В'] = "V", ['в'] = "v",
        ['Г'] = "G", ['г'] = "g",
        ['Д'] = "D", ['д'] = "d",
        ['Е'] = "E", ['е'] = "e",
        ['Ё'] = "Yo", ['ё'] = "yo",
        ['Ж'] = "Zh", ['ж'] = "zh",
        ['З'] = "Z", ['з'] = "z",
        ['И'] = "I", ['и'] = "i",
        ['Й'] = "Y", ['й'] = "y",
        ['К'] = "K", ['к'] = "k",
        ['Л'] = "L", ['л'] = "l",
        ['М'] = "M", ['м'] = "m",
        ['Н'] = "N", ['н'] = "n",
        ['О'] = "O", ['о'] = "o",
        ['П'] = "P", ['п'] = "p",
        ['Р'] = "R", ['р'] = "r",
        ['С'] = "S", ['с'] = "s",
        ['Т'] = "T", ['т'] = "t",
        ['У'] = "U", ['у'] = "u",
        ['Ф'] = "F", ['ф'] = "f",
        ['Х'] = "Kh", ['х'] = "kh",
        ['Ц'] = "Ts", ['ц'] = "ts",
        ['Ч'] = "Ch", ['ч'] = "ch",
        ['Ш'] = "Sh", ['ш'] = "sh",
        ['Щ'] = "Shch", ['щ'] = "shch",
        ['Ъ'] = "", ['ъ'] = "",
        ['Ы'] = "Y", ['ы'] = "y",
        ['Ь'] = "", ['ь'] = "",
        ['Э'] = "E", ['э'] = "e",
        ['Ю'] = "Yu", ['ю'] = "yu",
        ['Я'] = "Ya", ['я'] = "ya"
    };
}
