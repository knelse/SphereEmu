using System;
using System.Collections.Generic;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Server.UI.Localization;

/// <summary>
///     Item lore and display names. Russian suffix text prefers the Actual suffix map.
/// </summary>
public static class ItemLocaleText
{
    public static string DisplayName(ItemDbEntry item, Locale locale)
    {
        var name = CatalogName(item.Localization, item.SphereType, locale);
        var suffix = SuffixName(item.GameObjectType, item.Suffix, locale);
        return suffix is null ? name : $"{name} {suffix}";
    }

    public static string CatalogName(SphGameObject go, Locale locale) =>
        CatalogName(go.Localisation, go.SphereType, locale);

    public static string CatalogName(Dictionary<Locale, string> localization, string? sphereType, Locale locale)
    {
        return localization.GetValueOrDefault(locale)
               ?? localization.GetValueOrDefault(Locale.Russian)
               ?? sphereType
               ?? "?";
    }

    public static string GameObjectTypeName(GameObjectType type, Locale locale)
    {
        _ = locale;
        return type.ToString().Replace('_', ' ');
    }

    public static string? SuffixName(ItemDbEntry item, Locale locale) =>
        SuffixName(item.GameObjectType, item.Suffix, locale);

    public static string? SuffixName(GameObjectType type, ItemSuffix suffix, Locale locale)
    {
        if (suffix == ItemSuffix.None)
        {
            return null;
        }

        if (locale is Locale.Russian && TryActualSuffixName(type, suffix, locale, out var actual))
        {
            return actual;
        }

        var prefType = SphObjectDbHelper.GameObjectToPrefTypeMap.GetValueOrDefault(
            type, GameObjectType.Unknown);
        if (prefType is not GameObjectType.Unknown
            && SphObjectDb.SuffixDataDb.TryGetValue(prefType, out var bySuffix)
            && bySuffix.TryGetValue(suffix, out var suffixGo))
        {
            return suffixGo.Localisation.GetValueOrDefault(locale)
                   ?? suffixGo.Localisation.GetValueOrDefault(Locale.Russian)
                   ?? suffix.ToString();
        }

        return TryActualSuffixName(type, suffix, locale, out actual) ? actual : suffix.ToString();
    }

    private static bool TryActualSuffixName(GameObjectType type, ItemSuffix suffix, Locale locale,
        out string name)
    {
        name = string.Empty;
        if (!GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual.TryGetValue(type, out var map)
            || !map.TryGetValue(suffix, out var entry))
        {
            return false;
        }

        name = entry.localization.GetValueOrDefault(locale)
               ?? entry.localization.GetValueOrDefault(Locale.Russian)
               ?? string.Empty;
        return name.Length > 0;
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
