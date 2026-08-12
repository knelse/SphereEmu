using System.Collections.Generic;
using Godot;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     UI textures from <c>res://Godot/Textures/fx/</c> and <c>ui_custom/</c>.
///     Stats panel icons from composed <c>i_stat1</c>/<c>i_stat3</c>; popup req glyphs from <c>i_inf01</c>/<c>i_inf02</c>.
/// </summary>
public static class AdminUiAtlas
{
    private const string Fx = "res://Godot/Textures/fx/";
    private const string UiCustom = "res://Godot/Textures/ui_custom/";

    // Empty rounded item-slot frame on i_pup3
    private static readonly Rect2I SlotBorderRegion = new(164, 164, 40, 40);

    private const int GuildCell = 24;
    private static readonly Guild[] GuildIconOrder =
    [
        Guild.Assasin, Guild.Crusader, Guild.Inquisitor, Guild.Hunter, Guild.Archmage,
        Guild.Barbarian, Guild.Druid, Guild.Thief, Guild.MasterOfSteel, Guild.Armorer,
        Guild.Blacksmith, Guild.Warlock, Guild.Necromancer, Guild.Bandier
    ];

    // Icon crops from composed client panels (i_stat1 / i_stat3) — stats UI (with panel bg).
    private static readonly Rect2I TitleIconRegion = new(12, 36, 18, 22);
    private static readonly Rect2I DegreeIconRegion = new(13, 72, 16, 18);
    private static readonly Rect2I HpIconRegion = new(11, 143, 15, 14);
    private static readonly Rect2I MpIconRegion = new(11, 159, 15, 15);
    private static readonly Rect2I SatietyIconRegion = new(11, 175, 15, 15);
    private static readonly Rect2I PAtkIconRegion = new(10, 197, 16, 14);
    private static readonly Rect2I MAtkIconRegion = new(102, 196, 18, 18);
    private static readonly Rect2I PDefIconRegion = new(11, 222, 14, 14);
    private static readonly Rect2I MDefIconRegion = new(101, 222, 20, 16);

    private static readonly Rect2I StrengthIconRegion = new(14, 22, 14, 16);
    private static readonly Rect2I AgilityIconRegion = new(13, 50, 18, 16);
    private static readonly Rect2I AccuracyIconRegion = new(12, 74, 16, 16);
    private static readonly Rect2I EnduranceIconRegion = new(14, 104, 16, 16);
    private static readonly Rect2I EarthIconRegion = new(101, 24, 18, 14);
    private static readonly Rect2I AirIconRegion = new(102, 50, 16, 14);
    private static readonly Rect2I WaterIconRegion = new(102, 76, 16, 16);
    private static readonly Rect2I FireIconRegion = new(100, 100, 16, 18);

    // Plain glyphs for item popup requirements (i_inf01 / i_inf02).
    private static readonly Rect2I ReqTitleIconRegion = new(0, 33, 24, 19);
    private static readonly Rect2I ReqStrengthIconRegion = new(40, 33, 12, 15);
    private static readonly Rect2I ReqAgilityIconRegion = new(53, 33, 19, 15);
    private static readonly Rect2I ReqAccuracyIconRegion = new(73, 33, 17, 17);
    private static readonly Rect2I ReqEnduranceIconRegion = new(91, 33, 13, 15);
    private static readonly Rect2I ReqEarthIconRegion = new(105, 33, 15, 9);
    private static readonly Rect2I ReqAirIconRegion = new(105, 43, 13, 9);
    private static readonly Rect2I ReqWaterIconRegion = new(49, 43, 14, 11);
    private static readonly Rect2I ReqFireIconRegion = new(70, 48, 14, 17);
    private static readonly Rect2I KarmaReqIconRegion = new(25, 33, 14, 14);

    // Item popup chrome glyphs (tight crops; not strict 16×16 cells).
    private static readonly Rect2I RankIconRegion = new(72, 0, 13, 13);
    private static readonly Rect2I GameIdIconRegion = new(57, 0, 15, 12);
    private static readonly Rect2I WeightIconRegion = new(48, 26, 15, 16);
    private static readonly Rect2I DurabilityIconRegion = new(47, 49, 16, 15);
    private static readonly Rect2I CostIconRegion = new(73, 51, 17, 19);
    private static readonly Rect2I CloseButtonRegion = new(15, 76, 14, 14);

    private static Texture2D? icons02Src;
    private static Texture2D? stat1Src;
    private static Texture2D? stat3Src;
    private static Texture2D? pup3Src;
    private static Texture2D? inf01Src;
    private static Texture2D? inf02Src;
    private static Texture2D? ctrlsSrc;

    private static Texture2D? titleIcon;
    private static Texture2D? degreeIcon;
    private static Texture2D? hpIcon;
    private static Texture2D? mpIcon;
    private static Texture2D? satietyIcon;
    private static Texture2D? pAtkIcon;
    private static Texture2D? mAtkIcon;
    private static Texture2D? pDefIcon;
    private static Texture2D? mDefIcon;
    private static Texture2D? strengthIcon;
    private static Texture2D? agilityIcon;
    private static Texture2D? accuracyIcon;
    private static Texture2D? enduranceIcon;
    private static Texture2D? earthIcon;
    private static Texture2D? airIcon;
    private static Texture2D? waterIcon;
    private static Texture2D? fireIcon;
    private static Texture2D? reqTitleIcon;
    private static Texture2D? reqStrengthIcon;
    private static Texture2D? reqAgilityIcon;
    private static Texture2D? reqAccuracyIcon;
    private static Texture2D? reqEnduranceIcon;
    private static Texture2D? reqEarthIcon;
    private static Texture2D? reqAirIcon;
    private static Texture2D? reqWaterIcon;
    private static Texture2D? reqFireIcon;
    private static Texture2D? personaMaleBg;
    private static Texture2D? personaFemaleBg;
    private static Texture2D? personaSlotOverlay;
    private static Texture2D? inventoryBackground;
    private static Texture2D? mutatorPlaceholder;
    private static Texture2D? slotBorder;
    private static Texture2D? popupTop;
    private static Texture2D? popupMid;
    private static Texture2D? popupBottom;
    private static Texture2D? closeButton;
    private static Texture2D? rankIcon;
    private static Texture2D? gameIdIcon;
    private static Texture2D? weightIcon;
    private static Texture2D? karmaIcon;
    private static Texture2D? durabilityIcon;
    private static Texture2D? costIcon;
    private static readonly Dictionary<string, Texture2D?> itemIconCache = new();
    private static readonly Dictionary<string, Texture2D?> bonusIcons = new();
    private static bool loaded;

    public static Texture2D? TitleIcon => Ensure() ? titleIcon : null;
    public static Texture2D? DegreeIcon => Ensure() ? degreeIcon : null;
    public static Texture2D? HpIcon => Ensure() ? hpIcon : null;
    public static Texture2D? MpIcon => Ensure() ? mpIcon : null;
    public static Texture2D? SatietyIcon => Ensure() ? satietyIcon : null;
    public static Texture2D? PAtkIcon => Ensure() ? pAtkIcon : null;
    public static Texture2D? MAtkIcon => Ensure() ? mAtkIcon : null;
    public static Texture2D? PDefIcon => Ensure() ? pDefIcon : null;
    public static Texture2D? MDefIcon => Ensure() ? mDefIcon : null;
    public static Texture2D? StrengthIcon => Ensure() ? strengthIcon : null;
    public static Texture2D? AgilityIcon => Ensure() ? agilityIcon : null;
    public static Texture2D? AccuracyIcon => Ensure() ? accuracyIcon : null;
    public static Texture2D? EnduranceIcon => Ensure() ? enduranceIcon : null;
    public static Texture2D? EarthIcon => Ensure() ? earthIcon : null;
    public static Texture2D? AirIcon => Ensure() ? airIcon : null;
    public static Texture2D? WaterIcon => Ensure() ? waterIcon : null;
    public static Texture2D? FireIcon => Ensure() ? fireIcon : null;

    public static Texture2D? ReqTitleIcon => Ensure() ? reqTitleIcon : null;
    public static Texture2D? ReqStrengthIcon => Ensure() ? reqStrengthIcon : null;
    public static Texture2D? ReqAgilityIcon => Ensure() ? reqAgilityIcon : null;
    public static Texture2D? ReqAccuracyIcon => Ensure() ? reqAccuracyIcon : null;
    public static Texture2D? ReqEnduranceIcon => Ensure() ? reqEnduranceIcon : null;
    public static Texture2D? ReqEarthIcon => Ensure() ? reqEarthIcon : null;
    public static Texture2D? ReqAirIcon => Ensure() ? reqAirIcon : null;
    public static Texture2D? ReqWaterIcon => Ensure() ? reqWaterIcon : null;
    public static Texture2D? ReqFireIcon => Ensure() ? reqFireIcon : null;

    public static Texture2D? PersonaMaleBackground => Ensure() ? personaMaleBg : null;
    public static Texture2D? PersonaFemaleBackground => Ensure() ? personaFemaleBg : null;
    public static Texture2D? PersonaSlotOverlay => Ensure() ? personaSlotOverlay : null;
    public static Texture2D? InventoryBackground => Ensure() ? inventoryBackground : null;
    public static Texture2D? MutatorPlaceholder => Ensure() ? mutatorPlaceholder : null;
    public static Texture2D? GenericSlotBorder => Ensure() ? slotBorder : null;
    public static Texture2D? PopupTop => Ensure() ? popupTop : null;
    public static Texture2D? PopupMid => Ensure() ? popupMid : null;
    public static Texture2D? PopupBottom => Ensure() ? popupBottom : null;
    public static Texture2D? CloseButton => Ensure() ? closeButton : null;
    public static Texture2D? RankIcon => Ensure() ? rankIcon : null;
    public static Texture2D? GameIdIcon => Ensure() ? gameIdIcon : null;
    public static Texture2D? WeightIcon => Ensure() ? weightIcon : null;
    public static Texture2D? KarmaIcon => Ensure() ? karmaIcon : null;
    public static Texture2D? DurabilityIcon => Ensure() ? durabilityIcon : null;
    public static Texture2D? CostIcon => Ensure() ? costIcon : null;

    /// <summary>Bonus/malus combo glyph keyed like <c>maxhp+</c>, <c>patk-</c>.</summary>
    public static Texture2D? BonusIcon(string key)
    {
        Ensure();
        return bonusIcons.TryGetValue(key, out var tex) ? tex : null;
    }

    /// <summary>Inventory icon DDS named by <c>ModelNameInventory</c> under <c>Godot/Textures/</c>.</summary>
    public static Texture2D? ItemIcon(string? modelNameInventory)
    {
        Ensure();
        if (string.IsNullOrWhiteSpace(modelNameInventory))
        {
            return null;
        }

        var key = modelNameInventory.Trim();
        if (itemIconCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var tex = LoadPath($"res://Godot/Textures/{key}.dds");
        itemIconCache[key] = tex;
        return tex;
    }

    public static Texture2D? GuildIcon(Guild guild)
    {
        if (guild == Guild.None || !Ensure() || icons02Src is null)
        {
            return null;
        }

        var index = AtlasIndexForGuild(guild);
        if (index < 0)
        {
            return null;
        }

        var col = index % 5;
        var row = index / 5;
        return Slice(icons02Src, new Rect2I(col * GuildCell, row * GuildCell, GuildCell, GuildCell));
    }

    private static int AtlasIndexForGuild(Guild guild)
    {
        for (var i = 0; i < GuildIconOrder.Length; i++)
        {
            if (GuildIconOrder[i] != guild)
            {
                continue;
            }

            return guild switch
            {
                Guild.Necromancer => 13,
                Guild.Bandier => 12,
                _ => i
            };
        }

        return -1;
    }

    private static bool Ensure()
    {
        if (loaded)
        {
            return stat1Src is not null;
        }

        loaded = true;
        icons02Src = LoadFx("i_icons02.dds");
        stat1Src = LoadFx("i_stat1.dds");
        stat3Src = LoadFx("i_stat3.dds");
        pup3Src = LoadFx("i_pup3.dds");
        inf01Src = LoadFx("i_inf01.dds");
        inf02Src = LoadFx("i_inf02.dds");
        ctrlsSrc = LoadFx("i_ctrls.dds");

        titleIcon = Slice(stat1Src, TitleIconRegion);
        degreeIcon = Slice(stat1Src, DegreeIconRegion);
        hpIcon = Slice(stat1Src, HpIconRegion);
        mpIcon = Slice(stat1Src, MpIconRegion);
        satietyIcon = Slice(stat1Src, SatietyIconRegion);
        pAtkIcon = Slice(stat1Src, PAtkIconRegion);
        mAtkIcon = Slice(stat1Src, MAtkIconRegion);
        pDefIcon = Slice(stat1Src, PDefIconRegion);
        mDefIcon = Slice(stat1Src, MDefIconRegion);

        strengthIcon = Slice(stat3Src, StrengthIconRegion);
        agilityIcon = Slice(stat3Src, AgilityIconRegion);
        accuracyIcon = Slice(stat3Src, AccuracyIconRegion);
        enduranceIcon = Slice(stat3Src, EnduranceIconRegion);
        earthIcon = Slice(stat3Src, EarthIconRegion);
        airIcon = Slice(stat3Src, AirIconRegion);
        waterIcon = Slice(stat3Src, WaterIconRegion);
        fireIcon = Slice(stat3Src, FireIconRegion);

        reqTitleIcon = SliceRaw(inf01Src, ReqTitleIconRegion);
        reqStrengthIcon = SliceRaw(inf01Src, ReqStrengthIconRegion);
        reqAgilityIcon = SliceRaw(inf01Src, ReqAgilityIconRegion);
        reqAccuracyIcon = SliceRaw(inf01Src, ReqAccuracyIconRegion);
        reqEnduranceIcon = SliceRaw(inf01Src, ReqEnduranceIconRegion);
        reqEarthIcon = SliceRaw(inf01Src, ReqEarthIconRegion);
        reqAirIcon = SliceRaw(inf01Src, ReqAirIconRegion);
        reqWaterIcon = SliceRaw(inf02Src, ReqWaterIconRegion);
        reqFireIcon = SliceRaw(inf02Src, ReqFireIconRegion);

        rankIcon = SliceRaw(inf02Src, RankIconRegion);
        gameIdIcon = SliceRaw(inf02Src, GameIdIconRegion);
        weightIcon = SliceRaw(inf02Src, WeightIconRegion);
        karmaIcon = SliceRaw(inf01Src, KarmaReqIconRegion);
        durabilityIcon = SliceRaw(inf01Src, DurabilityIconRegion);
        costIcon = SliceRaw(inf01Src, CostIconRegion);

        bonusIcons.Clear();
        void Bonus(string key, Texture2D? atlas, int x, int y, int w, int h) =>
            bonusIcons[key] = SliceRaw(atlas, new Rect2I(x, y, w, h));

        Bonus("pdef-", inf01Src, 0, 71, 21, 15);
        Bonus("mdef-", inf01Src, 22, 70, 26, 17);
        Bonus("str+", inf01Src, 49, 65, 21, 15);
        Bonus("agi+", inf01Src, 71, 71, 26, 15);
        Bonus("acc+", inf01Src, 98, 69, 23, 17);
        Bonus("air-", inf01Src, 0, 87, 21, 12);
        Bonus("agi-", inf01Src, 22, 88, 25, 15);
        Bonus("str-", inf01Src, 49, 81, 20, 15);
        Bonus("acc-", inf01Src, 70, 87, 23, 17);
        Bonus("end-", inf01Src, 94, 87, 22, 15);
        Bonus("maxmp-", inf01Src, 70, 105, 22, 14);
        Bonus("matk+", inf01Src, 97, 103, 26, 19);

        Bonus("matk-", inf02Src, 20, 29, 27, 19);
        Bonus("mdef+", inf02Src, 64, 30, 26, 17);
        Bonus("patk-", inf02Src, 91, 32, 22, 14);
        Bonus("maxhp+", inf02Src, 0, 51, 21, 12);
        Bonus("maxmp+", inf02Src, 22, 48, 22, 14);
        Bonus("end+", inf02Src, 0, 64, 23, 15);
        Bonus("water+", inf02Src, 23, 63, 23, 12);
        Bonus("air+", inf02Src, 47, 55, 22, 12);
        Bonus("fire-", inf02Src, 0, 80, 22, 17);
        Bonus("water-", inf02Src, 23, 76, 23, 12);
        Bonus("patk+", inf02Src, 48, 83, 23, 14);
        Bonus("pdef+", inf02Src, 72, 81, 21, 15);
        Bonus("earth+", inf02Src, 24, 89, 23, 12);
        Bonus("earth-", inf02Src, 0, 98, 23, 13);
        Bonus("maxhp-", inf02Src, 48, 98, 21, 12);
        Bonus("fire+", inf02Src, 24, 102, 22, 17);

        personaMaleBg = LoadPath(UiCustom + "male_persona_bg.png");
        personaFemaleBg = LoadPath(UiCustom + "female_persona_bg.png");
        personaSlotOverlay = LoadPath(UiCustom + "slot_overlay.png");
        inventoryBackground = LoadPath(UiCustom + "inventory.png");
        mutatorPlaceholder = LoadPath(UiCustom + "mut_placeholder.png");
        popupTop = LoadPath(UiCustom + "popup_top.png");
        popupMid = LoadPath(UiCustom + "popup_mid.png");
        popupBottom = LoadPath(UiCustom + "popup_bottom.png");
        closeButton = SliceRaw(ctrlsSrc, CloseButtonRegion);
        slotBorder = SliceRaw(pup3Src, SlotBorderRegion);
        return stat1Src is not null;
    }

    private static Texture2D? LoadFx(string fileName) => LoadPath(Fx + fileName);

    private static Texture2D? LoadPath(string path)
    {
        if (ResourceLoader.Exists(path))
        {
            return ResourceLoader.Load<Texture2D>(path);
        }

        // Newly dropped files may not have .import yet — load pixels via Godot FileAccess (works for res:// too).
        if (!global::Godot.FileAccess.FileExists(path))
        {
            var globalPath = ProjectSettings.GlobalizePath(path);
            GD.PushWarning($"AdminUiAtlas: missing {path} (resolved {globalPath})");
            return null;
        }

        using var file = global::Godot.FileAccess.Open(path, global::Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PushWarning($"AdminUiAtlas: failed to open {path}");
            return null;
        }

        var bytes = file.GetBuffer((long)file.GetLength());
        var image = new Image();
        var err = image.LoadPngFromBuffer(bytes);
        if (err != Error.Ok)
        {
            err = image.LoadJpgFromBuffer(bytes);
        }

        if (err != Error.Ok)
        {
            err = image.LoadWebpFromBuffer(bytes);
        }

        if (err != Error.Ok)
        {
            GD.PushWarning($"AdminUiAtlas: failed to decode {path}: {err}");
            return null;
        }

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Crop without glyph knockout — for frames/borders that must keep their full cell.</summary>
    private static Texture2D? SliceRaw(Texture2D? atlas, Rect2I region)
    {
        if (atlas is null)
        {
            return null;
        }

        var image = atlas.GetImage();
        if (image is null)
        {
            GD.PushWarning("AdminUiAtlas: GetImage() returned null — cannot crop");
            return null;
        }

        if (image.IsCompressed())
        {
            image.Decompress();
        }

        var w = image.GetWidth();
        var h = image.GetHeight();
        var x = Mathf.Clamp(region.Position.X, 0, Math.Max(0, w - 1));
        var y = Mathf.Clamp(region.Position.Y, 0, Math.Max(0, h - 1));
        var rw = Mathf.Clamp(region.Size.X, 1, w - x);
        var rh = Mathf.Clamp(region.Size.Y, 1, h - y);
        return ImageTexture.CreateFromImage(image.GetRegion(new Rect2I(x, y, rw, rh)));
    }

    private static Texture2D? Slice(Texture2D? atlas, Rect2I region)
    {
        if (atlas is null)
        {
            return null;
        }

        var image = atlas.GetImage();
        if (image is null)
        {
            GD.PushWarning("AdminUiAtlas: GetImage() returned null — cannot crop");
            return null;
        }

        if (image.IsCompressed())
        {
            image.Decompress();
        }

        var w = image.GetWidth();
        var h = image.GetHeight();
        var x = Mathf.Clamp(region.Position.X, 0, Math.Max(0, w - 1));
        var y = Mathf.Clamp(region.Position.Y, 0, Math.Max(0, h - 1));
        var rw = Mathf.Clamp(region.Size.X, 1, w - x);
        var rh = Mathf.Clamp(region.Size.Y, 1, h - y);
        var cropped = image.GetRegion(new Rect2I(x, y, rw, rh));
        return ImageTexture.CreateFromImage(ExtractIcon(cropped));
    }

    /// <summary>
    ///     Knock out the composed-panel background and center the glyph on a transparent square
    ///     so icons match the client (no grey cell boxes, no top-left bias).
    /// </summary>
    private static Image ExtractIcon(Image src, int minSide = 18)
    {
        var sw = src.GetWidth();
        var sh = src.GetHeight();
        if (sw <= 0 || sh <= 0)
        {
            return src;
        }

        var bg = EstimatePanelBackground(src, sw, sh);
        const int bgEps = 45;

        var minX = sw;
        var minY = sh;
        var maxX = -1;
        var maxY = -1;
        for (var py = 0; py < sh; py++)
        {
            for (var px = 0; px < sw; px++)
            {
                if (IsNearColor(src.GetPixel(px, py), bg, bgEps))
                {
                    continue;
                }

                if (px < minX)
                {
                    minX = px;
                }

                if (py < minY)
                {
                    minY = py;
                }

                if (px > maxX)
                {
                    maxX = px;
                }

                if (py > maxY)
                {
                    maxY = py;
                }
            }
        }

        if (maxX < 0)
        {
            return src;
        }

        var cw = maxX - minX + 1;
        var ch = maxY - minY + 1;
        var side = Math.Max(Math.Max(cw, ch), minSide);
        var canvas = Image.CreateEmpty(side, side, false, Image.Format.Rgba8);
        canvas.Fill(Colors.Transparent);
        var ox = (side - cw) / 2;
        var oy = (side - ch) / 2;
        for (var py = 0; py < ch; py++)
        {
            for (var px = 0; px < cw; px++)
            {
                var c = src.GetPixel(minX + px, minY + py);
                if (IsNearColor(c, bg, bgEps))
                {
                    continue;
                }

                canvas.SetPixel(ox + px, oy + py, c);
            }
        }

        return canvas;
    }

    private static Color EstimatePanelBackground(Image src, int sw, int sh)
    {
        // Median of edge samples ≈ the dark wood/panel fill behind glyphs.
        var samples = new List<Color>(sw * 2 + sh * 2);
        for (var x = 0; x < sw; x++)
        {
            samples.Add(src.GetPixel(x, 0));
            samples.Add(src.GetPixel(x, sh - 1));
        }

        for (var y = 1; y < sh - 1; y++)
        {
            samples.Add(src.GetPixel(0, y));
            samples.Add(src.GetPixel(sw - 1, y));
        }

        samples.Sort((a, b) => (a.R8 + a.G8 + a.B8).CompareTo(b.R8 + b.G8 + b.B8));
        return samples[samples.Count / 2];
    }

    private static bool IsNearColor(Color c, Color refColor, int eps) =>
        Math.Abs(c.R8 - refColor.R8) + Math.Abs(c.G8 - refColor.G8) + Math.Abs(c.B8 - refColor.B8) <= eps;
}
