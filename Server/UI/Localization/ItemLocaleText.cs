using System;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Server.UI.Localization;

/// <summary>
///     Item lore and display names. Suffix text comes from
///     <see cref="SphObjectDb.SuffixDataDb"/> localisation entries (language prefs).
/// </summary>
public static class ItemLocaleText
{
    public static string DisplayName(ItemDbEntry item, Locale locale)
    {
        var name = item.Localization.GetValueOrDefault(locale)
                   ?? item.Localization.GetValueOrDefault(Locale.Russian)
                   ?? item.SphereType
                   ?? "?";

        var suffix = SuffixName(item, locale);
        return string.IsNullOrEmpty(suffix) ? name : $"{name} {suffix}";
    }

    public static string? SuffixName(ItemDbEntry item, Locale locale)
    {
        if (item.Suffix == ItemSuffix.None)
        {
            return null;
        }

        var prefType = SphObjectDbHelper.GameObjectToPrefTypeMap.GetValueOrDefault(
            item.GameObjectType, GameObjectType.Unknown);
        if (prefType is GameObjectType.Unknown
            || !SphObjectDb.SuffixDataDb.TryGetValue(prefType, out var bySuffix)
            || !bySuffix.TryGetValue(item.Suffix, out var suffixGo))
        {
            return item.Suffix.ToString();
        }

        return suffixGo.Localisation.GetValueOrDefault(locale)
               ?? suffixGo.Localisation.GetValueOrDefault(Locale.Russian)
               ?? item.Suffix.ToString();
    }

    public static string? Description(ItemDbEntry item, Locale locale)
    {
        if (string.IsNullOrWhiteSpace(item.SphereType))
        {
            return null;
        }

        return Description(item.SphereType, item.GameId, locale);
    }

    public static string? Description(string sphereType, int gameId, Locale locale)
    {
        if (!SphObjectDb.LocalisationContent.TryGetValue(sphereType, out var byLocale))
        {
            return null;
        }

        if (!byLocale.TryGetValue(locale, out var lines)
            && !byLocale.TryGetValue(Locale.Russian, out lines))
        {
            return null;
        }

        string? fileDefault = null;
        string? sectionDesc = null;
        var inMatchingSection = false;
        var seenSection = false;

        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var line = raw.TrimEnd();
            if (line.StartsWith('#'))
            {
                seenSection = true;
                inMatchingSection = SectionContainsGameId(line, gameId);
                continue;
            }

            if (!TryParseDescLine(line, out var text))
            {
                continue;
            }

            if (!seenSection)
            {
                fileDefault = text;
            }
            else if (inMatchingSection)
            {
                sectionDesc = text;
            }
        }

        var result = sectionDesc ?? fileDefault;
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    private static bool TryParseDescLine(string line, out string text)
    {
        text = string.Empty;
        if (line.Length < 5 || !line.StartsWith("100", StringComparison.Ordinal))
        {
            return false;
        }

        if (line[3] is not (' ' or '\t'))
        {
            return false;
        }

        text = line[4..].Trim();
        return text.Length > 0;
    }

    private static bool SectionContainsGameId(string hashLine, int gameId)
    {
        var body = hashLine[1..].Trim();
        var dash = body.IndexOf('-');
        if (dash > 0)
        {
            if (int.TryParse(body.AsSpan(0, dash), out var start)
                && int.TryParse(body.AsSpan(dash + 1), out var end))
            {
                return gameId >= start && gameId <= end;
            }

            return false;
        }

        return int.TryParse(body, out var id) && id == gameId;
    }
}
