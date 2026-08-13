using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using SphServer.Server.UI.Localization;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Shared item detail content (header, reqs, bonuses, lore) without popup chrome.
/// </summary>
public static class AdminUiItemDetails
{
    public const float PreferredFrameWidth = 293f;
    public const float ItemIconPx = 32f;
    public const float RowIconPx = 18f;
    public const float GuildIconPx = 24f;
    public const float BonusIconW = 27f;
    public const float BonusIconH = 19f;
    public const float RowEstimate = 20f;
    public const int ReqFontSize = 13;
    public const int MetaFontSize = 11;
    public const int DescFontSize = 10;

    private static readonly Color TextWhite = new(0.92f, 0.92f, 0.9f);
    private static readonly Color TextUnmet = new(0.95f, 0.28f, 0.28f);
    private static readonly Color TextBonus = new(0.45f, 0.85f, 0.4f);
    private static readonly Color TextMalus = new(0.4f, 0.65f, 0.95f);
    private static readonly Color DividerColor = new(0.55f, 0.5f, 0.42f, 0.85f);

    /// <summary>Clears <paramref name="box"/> and fills it with item detail rows.</summary>
    public static void Fill(
        VBoxContainer box, ItemDbEntry item, CharacterDbEntry? character, Locale locale)
    {
        foreach (var child in box.GetChildren())
        {
            child.QueueFree();
        }

        box.AddChild(BuildHeader(item, locale));

        var roman = ToRomanTier(item);
        if (!string.IsNullOrEmpty(roman))
        {
            box.AddChild(BuildIconTextRow(AdminUiAtlas.RankIcon, roman, TextWhite));
        }

        if (item.Durability > 0)
        {
            var currentDura = item.CurrentDurability > 0 ? item.CurrentDurability : item.Durability;
            box.AddChild(BuildIconTextRow(
                AdminUiAtlas.DurabilityIcon,
                $"{currentDura} / [{item.Durability}]",
                TextWhite));
        }

        box.AddChild(BuildIconTextRow(
            AdminUiAtlas.WeightIcon,
            (item.Weight / 1000.0).ToString("0.000", CultureInfo.InvariantCulture),
            TextWhite));

        item.RecalculateStatReqsFromBase();

        var hasReqs = false;
        foreach (var row in EnumerateRequirementRows(item, character, locale))
        {
            if (!hasReqs)
            {
                box.AddChild(MakeDivider());
                hasReqs = true;
            }

            box.AddChild(row);
        }

        var hasBonuses = false;
        foreach (var row in EnumerateBonusRows(item))
        {
            if (!hasBonuses)
            {
                box.AddChild(MakeDivider());
                hasBonuses = true;
            }

            box.AddChild(row);
        }

        var description = ItemLocaleText.Description(item, locale);
        if (!string.IsNullOrWhiteSpace(description))
        {
            box.AddChild(BuildDescriptionLabel(description));
        }

        box.AddChild(MakeDivider());
        box.AddChild(BuildIconTextRow(
            AdminUiAtlas.GameIdIcon,
            item.GameId.ToString(CultureInfo.InvariantCulture),
            TextWhite));
        box.AddChild(BuildIconTextRow(
            AdminUiAtlas.CostIcon,
            ResolveVendorCost(item).ToString(CultureInfo.InvariantCulture),
            TextWhite));
    }

    /// <summary>Approximate content height used to size popup mid tiles.</summary>
    public static float EstimateContentHeight(
        ItemDbEntry item, Locale locale, float contentWidth, float marginTop = 0f, float marginBottom = 0f)
    {
        item.RecalculateStatReqsFromBase();

        var rows = 1 + 1 + 1 + 2 + 1; // header ~2, weight, footer divider, game id, cost
        if (item.IsTierVisible() && item.Tier is >= 1 and <= 15)
        {
            rows++;
        }

        if (item.Durability > 0)
        {
            rows++;
        }

        var reqCount = CountRequirementRows(item);
        var bonusCount = CountBonusRows(item);
        rows += reqCount;
        rows += bonusCount;
        if (reqCount > 0)
        {
            rows++;
        }

        if (bonusCount > 0)
        {
            rows++;
        }

        var description = ItemLocaleText.Description(item, locale);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var charsPerLine = Mathf.Max(12, (int)(contentWidth / 7f));
            rows += Mathf.Max(1, Mathf.CeilToInt(description.Length / (float)charsPerLine));
        }

        return ItemIconPx + rows * RowEstimate + marginTop + marginBottom;
    }

    /// <summary>Base vendor cost with suffix % applied (suffix is the item prefix/affix).</summary>
    private static int ResolveVendorCost(ItemDbEntry item)
    {
        if (!SphObjectDb.GameObjectDataDb.TryGetValue(item.GameId, out var go))
        {
            return Math.Max(0, item.VendorCost);
        }

        var cost = go.VendorCost;
        if (item.Suffix != ItemSuffix.None)
        {
            var suffixObj = SphObjectDbHelper.GetSuffixObject(item.GameObjectType, item.Suffix, item.Tier);
            cost = cost * (100 + suffixObj.VendorCost) / 100;
        }

        return Math.Max(0, cost);
    }

    private static Control BuildDescriptionLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", TextWhite);
        label.AddThemeFontSizeOverride("font_size", DescFontSize);
        return label;
    }

    private static Control BuildHeader(ItemDbEntry item, Locale locale)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        row.AddChild(new TextureRect
        {
            Texture = AdminUiAtlas.ItemIcon(item.ModelNameInventory),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            CustomMinimumSize = new Vector2(ItemIconPx, ItemIconPx),
            MouseFilter = Control.MouseFilterEnum.Ignore
        });

        var nameLabel = new Label
        {
            Text = ItemLocaleText.DisplayName(item, locale),
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        nameLabel.AddThemeColorOverride("font_color", TextWhite);
        nameLabel.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(nameLabel);
        return row;
    }

    private static Control BuildIconTextRow(
        Texture2D? iconTex, string text, Color color, int fontSize = MetaFontSize,
        float iconW = RowIconPx, float iconH = RowIconPx)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);

        row.AddChild(new TextureRect
        {
            Texture = iconTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            CustomMinimumSize = new Vector2(iconW, iconH),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });

        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        row.AddChild(label);
        return row;
    }

    private static Control MakeDivider()
    {
        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pad.AddThemeConstantOverride("margin_top", 3);
        pad.AddThemeConstantOverride("margin_bottom", 3);
        pad.AddChild(new ColorRect
        {
            Color = DividerColor,
            CustomMinimumSize = new Vector2(0, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
        return pad;
    }

    private static int CountRequirementRows(ItemDbEntry item)
    {
        var n = 0;
        if (item.TitleMinusOne > 0)
        {
            n++;
        }

        if (item.DegreeMinusOne > 0)
        {
            n++;
        }

        if (item.MinKarmaLevel > 0 || item.MaxKarmaLevel > 0)
        {
            n++;
        }

        if (item.StrengthReq > 0)
        {
            n++;
        }

        if (item.AgilityReq > 0)
        {
            n++;
        }

        if (item.AccuracyReq > 0)
        {
            n++;
        }

        if (item.EnduranceReq > 0)
        {
            n++;
        }

        if (item.EarthReq > 0)
        {
            n++;
        }

        if (item.AirReq > 0)
        {
            n++;
        }

        if (item.WaterReq > 0)
        {
            n++;
        }

        if (item.FireReq > 0)
        {
            n++;
        }

        return n;
    }

    private static IEnumerable<Control> EnumerateRequirementRows(
        ItemDbEntry item, CharacterDbEntry? character, Locale locale)
    {
        if (item.TitleMinusOne > 0)
        {
            yield return StatReqRow(
                AdminUiAtlas.ReqTitleIcon, item.TitleMinusOne, character?.TitleMinusOne ?? 0, true);
        }

        if (item.DegreeMinusOne > 0)
        {
            yield return StatReqRow(
                AdminUiAtlas.DegreeIcon, item.DegreeMinusOne, character?.DegreeMinusOne ?? 0, true);
        }

        if (item.RequiredGuild is not Guild.None)
        {
            yield return GuildReqRow(item, character, locale);
        }

        if (item.MinKarmaLevel > 0 || item.MaxKarmaLevel > 0)
        {
            yield return KarmaReqRow(item, character, locale);
        }

        if (item.StrengthReq > 0)
        {
            yield return StatReqRow(AdminUiAtlas.ReqStrengthIcon, item.StrengthReq, character?.CurrentStrength ?? 0);
        }

        if (item.AgilityReq > 0)
        {
            yield return StatReqRow(AdminUiAtlas.ReqAgilityIcon, item.AgilityReq, character?.CurrentAgility ?? 0);
        }

        if (item.AccuracyReq > 0)
        {
            yield return StatReqRow(AdminUiAtlas.ReqAccuracyIcon, item.AccuracyReq, character?.CurrentAccuracy ?? 0);
        }

        if (item.EnduranceReq > 0)
        {
            yield return StatReqRow(AdminUiAtlas.ReqEnduranceIcon, item.EnduranceReq, character?.CurrentEndurance ?? 0);
        }

        if (item.EarthReq > 0)
        {
            yield return StatReqRow(AdminUiAtlas.ReqEarthIcon, item.EarthReq, character?.CurrentEarth ?? 0);
        }

        if (item.AirReq > 0)
        {
            yield return StatReqRow(AdminUiAtlas.ReqAirIcon, item.AirReq, character?.CurrentAir ?? 0);
        }

        if (item.WaterReq > 0)
        {
            yield return StatReqRow(AdminUiAtlas.ReqWaterIcon, item.WaterReq, character?.CurrentWater ?? 0);
        }

        if (item.FireReq > 0)
        {
            yield return StatReqRow(AdminUiAtlas.ReqFireIcon, item.FireReq, character?.CurrentFire ?? 0);
        }
    }

    private static int CountBonusRows(ItemDbEntry item)
    {
        var n = 0;
        foreach (var _ in EnumerateBonusValues(item))
        {
            n++;
        }

        return n;
    }

    private static IEnumerable<Control> EnumerateBonusRows(ItemDbEntry item)
    {
        foreach (var (key, value, inverted, forceBonus) in EnumerateBonusValues(item))
        {
            yield return BonusRow(key, value, inverted, forceBonus);
        }
    }

    private static IEnumerable<(string Key, int Value, bool Inverted, bool ForceBonus)> EnumerateBonusValues(
        ItemDbEntry item)
    {
        ResolveAttackStats(item, out var pAtk, out var pAtkUp, out var mAtk, out var mAtkUp);

        if (item.MaxHpUp != 0)
        {
            yield return ("maxhp", item.MaxHpUp, false, false);
        }

        if (item.MaxMpUp != 0)
        {
            yield return ("maxmp", item.MaxMpUp, false, false);
        }

        if (pAtkUp != 0)
        {
            yield return ("patk", pAtkUp, true, true);
        }
        else if (pAtk != 0)
        {
            yield return ("patk", pAtk, true, false);
        }

        if (item.PDefUp != 0)
        {
            yield return ("pdef", item.PDefUp, false, false);
        }

        if (item.MDefUp != 0)
        {
            yield return ("mdef", item.MDefUp, false, false);
        }

        if (item.StrengthUp != 0)
        {
            yield return ("str", item.StrengthUp, false, false);
        }

        if (item.AgilityUp != 0)
        {
            yield return ("agi", item.AgilityUp, false, false);
        }

        if (item.AccuracyUp != 0)
        {
            yield return ("acc", item.AccuracyUp, false, false);
        }

        if (item.EnduranceUp != 0)
        {
            yield return ("end", item.EnduranceUp, false, false);
        }

        if (item.EarthUp != 0)
        {
            yield return ("earth", item.EarthUp, false, false);
        }

        if (item.AirUp != 0)
        {
            yield return ("air", item.AirUp, false, false);
        }

        if (item.WaterUp != 0)
        {
            yield return ("water", item.WaterUp, false, false);
        }

        if (item.FireUp != 0)
        {
            yield return ("fire", item.FireUp, false, false);
        }

        if (mAtkUp != 0)
        {
            yield return ("matk", mAtkUp, true, true);
        }
        else if (mAtk != 0)
        {
            yield return ("matk", mAtk, true, false);
        }
    }

    /// <summary>
    ///     Rebuild weapon/gear attack columns from base GO + suffix so display is not
    ///     poisoned by legacy double-negated values in the item row.
    /// </summary>
    private static void ResolveAttackStats(
        ItemDbEntry item, out int pAtk, out int pAtkUp, out int mAtk, out int mAtkUp)
    {
        if (!SphObjectDb.GameObjectDataDb.TryGetValue(item.GameId, out var go))
        {
            pAtk = item.PAtkNegative;
            pAtkUp = item.PAtkUpNegative;
            mAtk = item.MAtkNegativeOrHeal;
            mAtkUp = item.MAtkUpNegative;
            return;
        }

        pAtk = go.PAtkNegative;
        pAtkUp = go.PAtkUpNegative;
        mAtk = go.MAtkNegativeOrHeal;
        mAtkUp = go.MAtkUpNegative;

        if (item.Suffix == ItemSuffix.None)
        {
            return;
        }

        var suffixObj = SphObjectDbHelper.GetSuffixObject(item.GameObjectType, item.Suffix, item.Tier);
        // Suffix PA/MA keep file signs (neg=up, pos=down). *UpNegative positives mean up.
        pAtk += suffixObj.PAtkNegative;
        mAtk += suffixObj.MAtkNegativeOrHeal;
        pAtkUp += suffixObj.PAtkUpNegative > 0 ? -suffixObj.PAtkUpNegative : suffixObj.PAtkUpNegative;
        mAtkUp += suffixObj.MAtkUpNegative > 0 ? -suffixObj.MAtkUpNegative : suffixObj.MAtkUpNegative;
    }

    private static Control BonusRow(string key, int stored, bool inverted, bool forceBonus)
    {
        bool beneficial;
        if (forceBonus)
        {
            beneficial = true;
        }
        else if (inverted)
        {
            beneficial = stored < 0;
        }
        else
        {
            beneficial = stored > 0;
        }

        string iconKey;
        if (inverted)
        {
            iconKey = beneficial ? $"{key}-" : $"{key}+";
        }
        else
        {
            iconKey = beneficial ? $"{key}+" : $"{key}-";
        }

        var color = beneficial ? TextBonus : TextMalus;
        var shown = Math.Abs(stored).ToString(CultureInfo.InvariantCulture);
        return BuildIconTextRow(
            AdminUiAtlas.BonusIcon(iconKey), shown, color, ReqFontSize, BonusIconW, BonusIconH);
    }

    private static Control StatReqRow(Texture2D? icon, int need, int have, bool asDisplayLevel = false)
    {
        var needShown = asDisplayLevel ? CharacterLocaleText.DisplayLevel(need) : need;
        var haveShown = asDisplayLevel ? CharacterLocaleText.DisplayLevel(have) : have;
        var met = have >= need;
        return BuildIconTextRow(icon, $"{needShown} : [{haveShown}]", met ? TextWhite : TextUnmet, ReqFontSize);
    }

    private static Control KarmaReqRow(ItemDbEntry item, CharacterDbEntry? character, Locale locale)
    {
        var min = item.MinKarmaLevel <= 0 ? KarmaTypes.Очень_Плохая : item.MinKarmaLevel;
        var max = item.MaxKarmaLevel <= 0 ? KarmaTypes.Благая : item.MaxKarmaLevel;
        if ((byte)min > (byte)max)
        {
            (min, max) = (max, min);
        }

        var names = new List<string>();
        for (var k = (byte)min; k <= (byte)max; k++)
        {
            names.Add(CharacterLocaleText.KarmaTypeName((KarmaTypes)k, locale).Trim());
        }

        var text = string.Join(", ", names);
        Color color;
        if (character is null)
        {
            color = TextWhite;
        }
        else
        {
            var karma = (byte)character.Karma;
            var met = karma >= (byte)min && karma <= (byte)max;
            color = met ? TextWhite : TextUnmet;
        }

        return BuildIconTextRow(AdminUiAtlas.KarmaIcon, text, color, ReqFontSize);
    }

    private static Control GuildReqRow(ItemDbEntry item, CharacterDbEntry? character, Locale locale)
    {
        var guildName = CharacterLocaleText.GuildName(item.RequiredGuild, locale);
        var rankName = CharacterLocaleText.GuildRankName(item.RequiredGuildRankMinusOne, false, locale);
        var text = string.IsNullOrWhiteSpace(rankName) ? guildName : $"{guildName} - {rankName}";
        var met = character is not null
                  && character.Guild == item.RequiredGuild
                  && character.GuildLevelMinusOne >= item.RequiredGuildRankMinusOne
                  && GuildCatalog.MeetsRankRequirements(
                      item.RequiredGuild, item.RequiredGuildRankMinusOne,
                      character.TitleMinusOne, character.DegreeMinusOne);
        var color = character is null || met ? TextWhite : TextUnmet;
        return BuildIconTextRow(
            AdminUiAtlas.GuildIcon(item.RequiredGuild), text, color, ReqFontSize, GuildIconPx, GuildIconPx);
    }

    private static string ToRomanTier(ItemDbEntry item)
    {
        if (!item.IsTierVisible())
        {
            return string.Empty;
        }

        return item.Tier switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
            7 => "VII",
            8 => "VIII",
            9 => "IX",
            10 => "X",
            11 => "XI",
            12 => "XII",
            13 => "XIII",
            14 => "XIV",
            15 => "XV",
            _ => string.Empty
        };
    }
}
