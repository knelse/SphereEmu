using System;
using System.Collections.Generic;

namespace SphServer.Server.UI.Localization;

/// <summary>
///     Looks up sparse <c>_sys</c> lines by 4-digit ID for the selected <see cref="Locale"/>.
/// </summary>
public static class SysLocalization
{
    private static readonly Dictionary<Locale, Dictionary<int, string>> Cache = new();

    public static string Get(int id, Locale locale)
    {
        EnsureLoaded(locale);
        return Cache[locale].TryGetValue(id, out var text) ? text : $"#{id:D4}";
    }

    public static string Format(int id, Locale locale, params object[] args)
    {
        var template = Get(id, locale);
        try
        {
            // _sys mixes printf (%s/%d) and rarely .NET format; prefer printf-style substitution.
            var result = template;
            foreach (var arg in args)
            {
                var text = arg?.ToString() ?? string.Empty;
                var idxS = result.IndexOf("%s", StringComparison.Ordinal);
                var idxD = result.IndexOf("%d", StringComparison.Ordinal);
                if (idxS >= 0 && (idxD < 0 || idxS < idxD))
                {
                    result = string.Concat(result.AsSpan(0, idxS), text, result.AsSpan(idxS + 2));
                }
                else if (idxD >= 0)
                {
                    result = string.Concat(result.AsSpan(0, idxD), text, result.AsSpan(idxD + 2));
                }
                else
                {
                    break;
                }
            }

            return result;
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static void EnsureLoaded(Locale locale)
    {
        if (Cache.ContainsKey(locale))
        {
            return;
        }

        var map = new Dictionary<int, string>();
        if (SphObjectDb.LocalisationContent.TryGetValue("_sys", out var byLocale)
            && byLocale.TryGetValue(locale, out var lines))
        {
            foreach (var line in lines)
            {
                if (line.Length < 5 || line[4] != ' ')
                {
                    continue;
                }

                if (!int.TryParse(line.AsSpan(0, 4), out var id))
                {
                    continue;
                }

                map[id] = line[5..].TrimEnd();
            }
        }

        Cache[locale] = map;
    }

    public static void ClearCache() => Cache.Clear();
}
