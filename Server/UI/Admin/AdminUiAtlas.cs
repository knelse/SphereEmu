using System.Collections.Generic;
using Godot;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     UI textures from <c>res://Godot/Textures/fx/</c> and <c>ui_custom/</c>.
///     Icons are cropped from the composed <c>i_stat1</c> / <c>i_stat3</c> panels (correct client art).
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

    // Icon crops from composed client panels (i_stat1 / i_stat3). Keep clear of text-field chrome.
    // Title = crown+sword; degree = orb/pin; MDef = casting hand (not a separate foot sprite).
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

    private static Texture2D? icons02Src;
    private static Texture2D? stat1Src;
    private static Texture2D? stat3Src;
    private static Texture2D? pup3Src;

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
    private static Texture2D? personaMaleBg;
    private static Texture2D? personaFemaleBg;
    private static Texture2D? personaSlotOverlay;
    private static Texture2D? mutatorPlaceholder;
    private static Texture2D? slotBorder;
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

    public static Texture2D? PersonaMaleBackground => Ensure() ? personaMaleBg : null;
    public static Texture2D? PersonaFemaleBackground => Ensure() ? personaFemaleBg : null;
    public static Texture2D? PersonaSlotOverlay => Ensure() ? personaSlotOverlay : null;
    public static Texture2D? MutatorPlaceholder => Ensure() ? mutatorPlaceholder : null;
    public static Texture2D? GenericSlotBorder => Ensure() ? slotBorder : null;

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

        personaMaleBg = LoadPath(UiCustom + "male_persona_bg.png");
        personaFemaleBg = LoadPath(UiCustom + "female_persona_bg.png");
        personaSlotOverlay = LoadPath(UiCustom + "slot_overlay.png");
        mutatorPlaceholder = LoadPath(UiCustom + "mut_placeholder.png");
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

        // Newly dropped files may not have .import yet — load pixels directly.
        var globalPath = ProjectSettings.GlobalizePath(path);
        if (!global::System.IO.File.Exists(globalPath))
        {
            GD.PushWarning($"AdminUiAtlas: missing {path} (resolved {globalPath})");
            return null;
        }

        var image = new Image();
        var err = image.Load(globalPath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"AdminUiAtlas: failed to load {path}: {err}");
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
