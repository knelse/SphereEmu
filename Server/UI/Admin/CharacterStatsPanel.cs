using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using SphServer.Client;
using SphServer.Server.UI.Localization;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Character panel layout mirrors the client stats window; labels are icon-only (no STR/HP text).
/// </summary>
public partial class CharacterStatsPanel : PanelContainer
{
    private const int IconPx = 18;

    private Locale locale = Locale.Russian;
    private ushort? selectedClientId;
    private Label? nameLabel;
    private Label? titleLabel;
    private ProgressBar? titleXpBar;
    private Label? titleXpLabel;
    private Label? degreeLabel;
    private ProgressBar? degreeXpBar;
    private Label? degreeXpLabel;
    private TextureRect? guildIcon;
    private Label? guildLabel;
    private Label? clanLabel;
    private Label? karmaLabel;
    private ProgressBar? hpBar;
    private Label? hpLabel;
    private ProgressBar? mpBar;
    private Label? mpLabel;
    private ProgressBar? satietyBar;
    private Label? satietyLabel;
    private Label? pAtkLabel;
    private Label? mAtkLabel;
    private Label? pDefLabel;
    private Label? mDefLabel;
    private Label? availableTitleLabel;
    private Label? availableDegreeLabel;
    private Label? xLabel;
    private Label? yLabel;
    private Label? zLabel;
    private Label? angleLabel;
    private readonly Dictionary<Stat, StatRow> statRows = new();
    private bool suppressStatCallbacks;

    private sealed class StatRow
    {
        public required LineEdit Edit { get; init; }
        public required Stat Stat { get; init; }
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(300, 0);
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.14f, 0.11f, 0.09f, 0.96f),
            BorderColor = new Color(0.55f, 0.42f, 0.25f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 10,
            ContentMarginTop = 8,
            ContentMarginRight = 10,
            ContentMarginBottom = 10
        });

        var outer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        outer.AddThemeConstantOverride("separation", 6);
        AddChild(outer);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        outer.AddChild(scroll);

        var root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(root);

        nameLabel = new Label { HorizontalAlignment = HorizontalAlignment.Left };
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(nameLabel);
        root.AddChild(MakeDivider());

        AddTierRow(root, AdminUiAtlas.TitleIcon, out titleLabel, out titleXpLabel, out titleXpBar);
        AddTierRow(root, AdminUiAtlas.DegreeIcon, out degreeLabel, out degreeXpLabel, out degreeXpBar);

        clanLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        root.AddChild(clanLabel);

        var guildRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        guildRow.AddThemeConstantOverride("separation", 6);
        guildIcon = MakeIconRect(null);
        guildIcon.Visible = false;
        guildLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        guildRow.AddChild(guildIcon);
        guildRow.AddChild(guildLabel);
        root.AddChild(guildRow);

        karmaLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        root.AddChild(karmaLabel);
        root.AddChild(MakeDivider());

        AddVitalRow(root, AdminUiAtlas.HpIcon, new Color(0.25f, 0.75f, 0.2f), out hpBar, out hpLabel);
        AddVitalRow(root, AdminUiAtlas.MpIcon, new Color(0.25f, 0.55f, 0.95f), out mpBar, out mpLabel);
        AddVitalRow(root, AdminUiAtlas.SatietyIcon, new Color(0.9f, 0.75f, 0.2f), out satietyBar, out satietyLabel);
        root.AddChild(MakeDivider());

        var combat = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        combat.AddThemeConstantOverride("h_separation", 24);
        combat.AddThemeConstantOverride("v_separation", 6);
        pAtkLabel = AddIconValueCell(combat, AdminUiAtlas.PAtkIcon);
        mAtkLabel = AddIconValueCell(combat, AdminUiAtlas.MAtkIcon);
        pDefLabel = AddIconValueCell(combat, AdminUiAtlas.PDefIcon);
        mDefLabel = AddIconValueCell(combat, AdminUiAtlas.MDefIcon);
        root.AddChild(combat);
        root.AddChild(MakeDivider());

        var available = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        available.AddThemeConstantOverride("h_separation", 24);
        availableTitleLabel = AddIconValueCell(available, AdminUiAtlas.TitleIcon);
        availableDegreeLabel = AddIconValueCell(available, AdminUiAtlas.DegreeIcon);
        root.AddChild(available);
        root.AddChild(MakeDivider());

        var statsColumns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        statsColumns.AddThemeConstantOverride("separation", 16);
        var titleCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var degreeCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titleCol.AddThemeConstantOverride("separation", 4);
        degreeCol.AddThemeConstantOverride("separation", 4);
        AddStatEditor(titleCol, Stat.Strength, AdminUiAtlas.StrengthIcon);
        AddStatEditor(titleCol, Stat.Agility, AdminUiAtlas.AgilityIcon);
        AddStatEditor(titleCol, Stat.Accuracy, AdminUiAtlas.AccuracyIcon);
        AddStatEditor(titleCol, Stat.Endurance, AdminUiAtlas.EnduranceIcon);
        AddStatEditor(degreeCol, Stat.Earth, AdminUiAtlas.EarthIcon);
        AddStatEditor(degreeCol, Stat.Air, AdminUiAtlas.AirIcon);
        AddStatEditor(degreeCol, Stat.Water, AdminUiAtlas.WaterIcon);
        AddStatEditor(degreeCol, Stat.Fire, AdminUiAtlas.FireIcon);
        statsColumns.AddChild(titleCol);
        statsColumns.AddChild(degreeCol);
        root.AddChild(statsColumns);

        outer.AddChild(MakeDivider());
        var posRow = new GridContainer
        {
            Columns = 4,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        posRow.AddThemeConstantOverride("h_separation", 12);
        xLabel = AddLabeledValue(posRow, "X");
        yLabel = AddLabeledValue(posRow, "Y");
        zLabel = AddLabeledValue(posRow, "Z");
        angleLabel = AddLabeledValue(posRow, "Angle");
        outer.AddChild(posRow);
    }

    public void SetLocale(Locale newLocale)
    {
        locale = newLocale;
        Refresh();
    }

    public void SetSelectedClient(ushort? clientId)
    {
        selectedClientId = clientId;
        if (selectedClientId is not null)
        {
            ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter?.RecalcCurrentStats();
        }

        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        if (nameLabel is null)
        {
            return;
        }

        var character = selectedClientId is null
            ? null
            : ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter;

        if (character is null)
        {
            ClearDisplay();
            return;
        }

        character.SyncKarmaFromCount();

        nameLabel.Text = character.Name;
        titleLabel!.Text = CharacterLocaleText.TitleName(character, locale);
        degreeLabel!.Text = CharacterLocaleText.DegreeName(character, locale);

        var xpToLevel = Math.Max(1UL, character.XpToLevelUp);
        SetBar(titleXpBar!, titleXpLabel!, character.TitleXP, xpToLevel);
        SetBar(degreeXpBar!, degreeXpLabel!, character.DegreeXP, xpToLevel);

        clanLabel!.Text = CharacterLocaleText.ClanLine(character, locale);

        var guildName = CharacterLocaleText.GuildName(character, locale);
        var rankName = CharacterLocaleText.GuildRankName(character, locale);
        var hasGuild = character.Guild != Guild.None;
        guildIcon!.Texture = hasGuild ? AdminUiAtlas.GuildIcon(character.Guild) : null;
        guildIcon.Visible = hasGuild;
        guildLabel!.Text = hasGuild ? $"{guildName} — {rankName}" : guildName;
        karmaLabel!.Text = CharacterLocaleText.KarmaLine(character, locale);

        SetBar(hpBar!, hpLabel!, character.CurrentHP, character.MaxHP);
        SetBar(mpBar!, mpLabel!, character.CurrentMP, character.MaxMP);
        SetBar(satietyBar!, satietyLabel!, character.CurrentSatiety, character.MaxSatiety);

        pAtkLabel!.Text = character.PAtk.ToString(CultureInfo.InvariantCulture);
        mAtkLabel!.Text = character.MAtk.ToString(CultureInfo.InvariantCulture);
        pDefLabel!.Text = character.PDef.ToString(CultureInfo.InvariantCulture);
        mDefLabel!.Text = character.MDef.ToString(CultureInfo.InvariantCulture);

        availableTitleLabel!.Text = character.AvailableTitleStats.ToString(CultureInfo.InvariantCulture);
        availableDegreeLabel!.Text = character.AvailableDegreeStats.ToString(CultureInfo.InvariantCulture);

        xLabel!.Text = character.X.ToString("F1", CultureInfo.InvariantCulture);
        yLabel!.Text = character.Y.ToString("F1", CultureInfo.InvariantCulture);
        zLabel!.Text = character.Z.ToString("F1", CultureInfo.InvariantCulture);
        angleLabel!.Text = character.Angle.ToString("F1", CultureInfo.InvariantCulture);

        suppressStatCallbacks = true;
        foreach (var (stat, row) in statRows)
        {
            if (!row.Edit.HasFocus())
            {
                row.Edit.Text = character.GetCurrentStat(stat).ToString(CultureInfo.InvariantCulture);
            }

            row.Edit.Editable = true;
        }

        suppressStatCallbacks = false;
    }

    private void ClearDisplay()
    {
        nameLabel!.Text = string.Empty;
        titleLabel!.Text = string.Empty;
        degreeLabel!.Text = string.Empty;
        SetBar(titleXpBar!, titleXpLabel!, 0, 1);
        SetBar(degreeXpBar!, degreeXpLabel!, 0, 1);
        guildIcon!.Texture = null;
        guildIcon.Visible = false;
        guildLabel!.Text = CharacterLocaleText.EmptyGuildName(locale);
        clanLabel!.Text = CharacterLocaleText.ClanLine(null, locale);
        karmaLabel!.Text = CharacterLocaleText.KarmaLine(null, locale);
        SetBar(hpBar!, hpLabel!, 0, 1);
        SetBar(mpBar!, mpLabel!, 0, 1);
        SetBar(satietyBar!, satietyLabel!, 0, 1);
        pAtkLabel!.Text = string.Empty;
        mAtkLabel!.Text = string.Empty;
        pDefLabel!.Text = string.Empty;
        mDefLabel!.Text = string.Empty;
        availableTitleLabel!.Text = string.Empty;
        availableDegreeLabel!.Text = string.Empty;
        xLabel!.Text = "<empty>";
        yLabel!.Text = "<empty>";
        zLabel!.Text = "<empty>";
        angleLabel!.Text = "<empty>";

        suppressStatCallbacks = true;
        foreach (var row in statRows.Values)
        {
            row.Edit.Text = string.Empty;
            row.Edit.Editable = false;
        }

        suppressStatCallbacks = false;
    }

    private static HSeparator MakeDivider() => new();

    private static TextureRect MakeIconRect(Texture2D? texture) => new()
    {
        Texture = texture,
        CustomMinimumSize = new Vector2(IconPx, IconPx),
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        SizeFlagsVertical = SizeFlags.ShrinkCenter,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        TextureFilter = TextureFilterEnum.Nearest
    };

    private static void AddTierRow(Control parent, Texture2D? icon, out Label name, out Label xpText,
        out ProgressBar bar)
    {
        var block = new VBoxContainer();
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        top.AddChild(MakeIconRect(icon));
        name = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        xpText = new Label();
        top.AddChild(name);
        top.AddChild(xpText);
        block.AddChild(top);
        bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, 8),
            ShowPercentage = false,
            MaxValue = 1,
            Value = 0
        };
        block.AddChild(bar);
        parent.AddChild(block);
    }

    private static void AddVitalRow(Control parent, Texture2D? icon, Color fill, out ProgressBar bar, out Label text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(MakeIconRect(icon));
        bar = new ProgressBar
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 14),
            ShowPercentage = false
        };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        text = new Label { CustomMinimumSize = new Vector2(72, 0), HorizontalAlignment = HorizontalAlignment.Right };
        row.AddChild(bar);
        row.AddChild(text);
        parent.AddChild(row);
    }

    private static Label AddIconValueCell(GridContainer grid, Texture2D? icon)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(MakeIconRect(icon));
        var label = new Label { CustomMinimumSize = new Vector2(40, 0) };
        row.AddChild(label);
        grid.AddChild(row);
        return label;
    }

    private static Label AddLabeledValue(GridContainer grid, string title)
    {
        var cell = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cell.AddThemeConstantOverride("separation", 4);
        var titleLabel = new Label { Text = title };
        titleLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.7f, 0.55f));
        var value = new Label
        {
            Text = "<empty>",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        cell.AddChild(titleLabel);
        cell.AddChild(value);
        grid.AddChild(cell);
        return value;
    }

    private void AddStatEditor(Control parent, Stat stat, Texture2D? icon)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(MakeIconRect(icon));
        var edit = new LineEdit
        {
            CustomMinimumSize = new Vector2(48, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = HorizontalAlignment.Center
        };
        edit.TextSubmitted += _ => CommitStat(stat, edit);
        edit.FocusExited += () => CommitStat(stat, edit);
        var minus = new Button { Text = "−", CustomMinimumSize = new Vector2(28, 0) };
        var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(28, 0) };
        minus.Pressed += () => NudgeStat(stat, -1);
        plus.Pressed += () => NudgeStat(stat, 1);
        row.AddChild(edit);
        row.AddChild(minus);
        row.AddChild(plus);
        parent.AddChild(row);
        statRows[stat] = new StatRow { Edit = edit, Stat = stat };
    }

    private void NudgeStat(Stat stat, int delta)
    {
        var client = GetSelectedClient();
        var character = client?.CurrentCharacter;
        if (client is null || character is null)
        {
            return;
        }

        var oldValue = character.GetCurrentStat(stat);
        if (!character.ApplyCurrentStatEdit(stat, oldValue + delta))
        {
            return;
        }

        AdminActionLog.Info(client, $"changed {stat} from {oldValue} to {character.GetCurrentStat(stat)}");
        Refresh();
    }

    private void CommitStat(Stat stat, LineEdit edit)
    {
        if (suppressStatCallbacks)
        {
            return;
        }

        var client = GetSelectedClient();
        var character = client?.CurrentCharacter;
        if (client is null || character is null)
        {
            return;
        }

        if (!int.TryParse(edit.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            edit.Text = character.GetCurrentStat(stat).ToString(CultureInfo.InvariantCulture);
            return;
        }

        var oldValue = character.GetCurrentStat(stat);
        if (!character.ApplyCurrentStatEdit(stat, value))
        {
            return;
        }

        AdminActionLog.Info(client, $"changed {stat} from {oldValue} to {character.GetCurrentStat(stat)}");
        Refresh();
    }

    private SphereClient? GetSelectedClient() =>
        selectedClientId is null ? null : ActiveClients.Get(selectedClientId.Value);

    private static void SetBar(ProgressBar bar, Label label, double current, double max)
    {
        var safeMax = Math.Max(1.0, max);
        bar.MaxValue = safeMax;
        bar.Value = Math.Clamp(current, 0, safeMax);
        label.Text = $"{current} / {max}";
    }
}
