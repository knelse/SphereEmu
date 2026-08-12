using System;
using System.Collections.Generic;
using System.Globalization;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Server.UI.Localization;

public static class CharacterLocaleText
{
    private static readonly Guild[] GuildNameOrder =
    [
        Guild.Assasin, Guild.Crusader, Guild.Inquisitor, Guild.Hunter, Guild.Archmage,
        Guild.Barbarian, Guild.Druid, Guild.Thief, Guild.MasterOfSteel, Guild.Armorer,
        Guild.Blacksmith, Guild.Warlock, Guild.Necromancer, Guild.Bandier
    ];

    public static int DisplayLevel(int minusOne) => minusOne % 60 + 1;

    public static int RebirthCount(int minusOne) => minusOne / 60;

    public static string TitleName(CharacterDbEntry character, Locale locale)
    {
        return TierName(character.TitleMinusOne, character.IsGenderFemale, 300, locale);
    }

    public static string DegreeName(CharacterDbEntry character, Locale locale)
    {
        return TierName(character.DegreeMinusOne, character.IsGenderFemale, 500, locale);
    }

    public static string GuildRankName(CharacterDbEntry character, Locale locale)
    {
        var genderOffset = character.IsGenderFemale ? 1 : 0;
        var id = 222 + character.GuildLevelMinusOne * 2 + genderOffset;
        return SysLocalization.Get(id, locale);
    }

    public static string ClanLine(CharacterDbEntry? character, Locale locale)
    {
        var name = string.Empty;
        var rank = string.Empty;
        if (character is not null && HasClan(character))
        {
            name = character.Clan!.Name;
            rank = ClanRankName(character, locale);
        }

        return SysLocalization.Format(130, locale, name, rank);
    }

    public static string ClanRankName(CharacterDbEntry character, Locale locale)
    {
        if (character.ClanRank > ClanRank.Neophyte)
        {
            return string.Empty;
        }

        var genderOffset = character.IsGenderFemale ? 1 : 0;
        var id = 141 + (int)character.ClanRank * 2 + genderOffset;
        return SysLocalization.Get(id, locale);
    }

    public static bool HasClan(CharacterDbEntry character) =>
        character.Clan is not null && character.Clan.Id != ClanDbEntry.DefaultClanDbEntry.Id;

    private static readonly Dictionary<Locale, string> EmptyGuildLabelCache = new();

    public static string EmptyGuildName(Locale locale)
    {
        if (locale == Locale.English)
        {
            return "Guild";
        }

        if (EmptyGuildLabelCache.TryGetValue(locale, out var cached))
        {
            return cached;
        }

        var label = "Guild";
        if (SphObjectDb.LocalisationContent.TryGetValue("guild", out var byLocale)
            && byLocale.TryGetValue(locale, out var lines))
        {
            foreach (var line in lines)
            {
                // Prefer short form lines ("03 гилд ...") — first word is the guild noun.
                if (!line.StartsWith("03 ", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = line.AsSpan(3).Trim();
                var space = text.IndexOf(' ');
                label = CapitalizeFirst((space < 0 ? text : text[..space]).ToString());
                break;
            }
        }

        EmptyGuildLabelCache[locale] = label;
        return label;
    }

    private static string CapitalizeFirst(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
    }

    public static string GuildName(CharacterDbEntry character, Locale locale)
    {
        if (character.Guild == Guild.None)
        {
            return EmptyGuildName(locale);
        }

        for (var i = 0; i < GuildNameOrder.Length; i++)
        {
            if (GuildNameOrder[i] != character.Guild)
            {
                continue;
            }

            return SysLocalization.Get(900 + i, locale);
        }

        return character.Guild.ToString();
    }

    public static string KarmaTypeName(KarmaTypes karma, Locale locale)
    {
        // KarmaTypes 1..5 → _sys 0090..0094
        return SysLocalization.Get(89 + (int)karma, locale);
    }

    public static string KarmaLine(CharacterDbEntry? character, Locale locale)
    {
        if (character is null)
        {
            return $"{SysLocalization.Format(89, locale, string.Empty)} (0)";
        }

        character.SyncKarmaFromCount();
        var typeId = 89 + (int)character.Karma; // KarmaTypes 1..5 → 0090..0094
        var typeName = SysLocalization.Get(typeId, locale);
        var formatted = SysLocalization.Format(89, locale, typeName);
        return $"{formatted} ({character.KarmaCount})";
    }

    private static string TierName(int minusOne, bool female, int baseId, Locale locale)
    {
        var display = DisplayLevel(minusOne);
        var rebirth = RebirthCount(minusOne);
        var genderOffset = female ? 1 : 0;
        var nameId = (display - 1) * 2 + baseId + genderOffset;
        var name = SysLocalization.Get(nameId, locale);

        if (rebirth <= 0)
        {
            return name;
        }

        var prefixId = 650 + (rebirth - 1) * 2 + genderOffset;
        var prefix = SysLocalization.Get(prefixId, locale);
        // Client shows "prefix name" but _sys name already includes "(N)"; keep prefix + name.
        return $"{prefix} {name}";
    }
}
