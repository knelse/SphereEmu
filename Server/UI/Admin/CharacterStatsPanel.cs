using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using SphServer.Client;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Helpers;
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
    private static readonly Color GuildUnmetText = new(0.95f, 0.28f, 0.28f);

    private Locale locale = Locale.Russian;
    private ushort? selectedClientId;
    private Label? nameLabel;
    private Label? titleLabel;
    private ProgressBar? titleXpBar;
    private LineEdit? titleXpEdit;
    private Label? titleXpLabel;
    private Label? degreeLabel;
    private ProgressBar? degreeXpBar;
    private LineEdit? degreeXpEdit;
    private Label? degreeXpLabel;
    private OptionButton? guildSelect;
    private OptionButton? rankSelect;
    private Label? guildLabel;
    private Locale guildDropdownsLocale;
    private bool? guildDropdownsFemale;
    private Label? clanLabel;
    private Label? karmaLabel;
    private LineEdit? karmaEdit;
    private bool karmaEditing;
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
    private LineEdit? titleLevelEdit;
    private LineEdit? degreeLevelEdit;
    private Control? statConfirmRow;
    private StatEditSnapshot? statEditSnapshot;
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

    private sealed class StatEditSnapshot
    {
        public int BaseStrength { get; init; }
        public int BaseAgility { get; init; }
        public int BaseAccuracy { get; init; }
        public int BaseEndurance { get; init; }
        public int BaseEarth { get; init; }
        public int BaseAir { get; init; }
        public int BaseWater { get; init; }
        public int BaseFire { get; init; }
        public int AvailableTitleStats { get; init; }
        public int AvailableDegreeStats { get; init; }
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

        AddTierRow(root, AdminUiAtlas.TitleIcon, isTitle: true, out titleLabel, out titleXpLabel, out titleXpBar);
        AddTierRow(root, AdminUiAtlas.DegreeIcon, isTitle: false, out degreeLabel, out degreeXpLabel, out degreeXpBar);

        clanLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        root.AddChild(clanLabel);

        var guildRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        guildRow.AddThemeConstantOverride("separation", 6);
        guildLabel = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        guildSelect = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FitToLongestItem = false
        };
        guildSelect.AddThemeConstantOverride("icon_max_width", 24);
        guildSelect.GetPopup().AddThemeColorOverride("font_disabled_color", GuildUnmetText);
        guildSelect.ItemSelected += OnGuildSelected;
        rankSelect = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FitToLongestItem = false
        };
        rankSelect.GetPopup().AddThemeColorOverride("font_disabled_color", GuildUnmetText);
        rankSelect.ItemSelected += OnRankSelected;
        guildRow.AddChild(guildLabel);
        guildRow.AddChild(guildSelect);
        guildRow.AddChild(rankSelect);
        root.AddChild(guildRow);

        karmaLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Ibeam
        };
        karmaLabel.GuiInput += OnKarmaLabelGuiInput;
        root.AddChild(karmaLabel);

        karmaEdit = new LineEdit
        {
            Visible = false,
            Alignment = HorizontalAlignment.Center,
            SelectAllOnFocus = true
        };
        karmaEdit.TextSubmitted += _ => CommitKarmaEdit();
        karmaEdit.FocusExited += CommitKarmaEdit;
        root.AddChild(karmaEdit);
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

        var statsColumns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        statsColumns.AddThemeConstantOverride("separation", 16);
        var titleCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var degreeCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titleCol.AddThemeConstantOverride("separation", 4);
        degreeCol.AddThemeConstantOverride("separation", 4);
        availableTitleLabel = AddCenteredIconValue(titleCol, AdminUiAtlas.TitleIcon);
        availableDegreeLabel = AddCenteredIconValue(degreeCol, AdminUiAtlas.DegreeIcon);
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

        statConfirmRow = new VBoxContainer { Visible = false };
        statConfirmRow.AddChild(MakeDivider());
        var confirmButtons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        confirmButtons.AddThemeConstantOverride("separation", 16);
        var submit = MakeAtlasButton(AdminUiAtlas.SubmitButton);
        var cancel = MakeAtlasButton(AdminUiAtlas.CancelButton);
        submit.Pressed += SubmitStatEdits;
        cancel.Pressed += CancelStatEdits;
        confirmButtons.AddChild(submit);
        confirmButtons.AddChild(cancel);
        statConfirmRow.AddChild(confirmButtons);
        root.AddChild(statConfirmRow);

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

        ClientStateEvents.CharacterChanged += OnCharacterChanged;
        ClientStateEvents.RosterChanged += OnRosterChanged;
    }

    public void SetLocale(Locale newLocale)
    {
        locale = newLocale;
        RequestRefresh();
    }

    public void SetSelectedClient(ushort? clientId)
    {
        if (statEditSnapshot is not null && selectedClientId != clientId)
        {
            CancelStatEdits();
        }

        if (selectedClientId != clientId)
        {
            EndKarmaEditDisplay();
        }

        selectedClientId = clientId;
        if (selectedClientId is not null)
        {
            ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter?.RecalcCurrentStats();
        }

        RequestRefresh();
    }

    public override void _ExitTree()
    {
        ClientStateEvents.CharacterChanged -= OnCharacterChanged;
        ClientStateEvents.RosterChanged -= OnRosterChanged;
    }

    private void OnCharacterChanged(ushort clientId)
    {
        if (selectedClientId == clientId)
        {
            RequestRefresh();
        }
    }

    private void OnRosterChanged()
    {
        if (selectedClientId is not null && ActiveClients.Get(selectedClientId.Value) is null)
        {
            selectedClientId = null;
            ClearStatEditSession();
            RequestRefresh();
        }
    }

    private bool refreshPending;

    private void RequestRefresh()
    {
        if (refreshPending)
        {
            return;
        }

        refreshPending = true;
        CallDeferred(nameof(DeferredRefresh));
    }

    private void DeferredRefresh()
    {
        refreshPending = false;
        Refresh();
    }

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
        clanLabel!.Text = CharacterLocaleText.ClanLine(character, locale);

        FillGuildDropdowns(character.IsGenderFemale);
        SelectGuildDropdowns(character.Guild, character.GuildLevelMinusOne, enabled: true);
        ColorGuildDropdowns(character);
        if (!karmaEditing)
        {
            karmaLabel!.Text = CharacterLocaleText.KarmaLine(character, locale);
        }

        SetBar(hpBar!, hpLabel!, character.CurrentHP, character.MaxHP);
        SetBar(mpBar!, mpLabel!, character.CurrentMP, character.MaxMP);
        SetBar(satietyBar!, satietyLabel!, character.CurrentSatiety, character.MaxSatiety);

        // Stored atk is negative (client convention); show magnitude for the admin UI.
        pAtkLabel!.Text = (-character.PAtk).ToString(CultureInfo.InvariantCulture);
        mAtkLabel!.Text = (-character.MAtk).ToString(CultureInfo.InvariantCulture);
        pDefLabel!.Text = character.PDef.ToString(CultureInfo.InvariantCulture);
        mDefLabel!.Text = character.MDef.ToString(CultureInfo.InvariantCulture);

        availableTitleLabel!.Text = character.AvailableTitleStats.ToString(CultureInfo.InvariantCulture);
        availableDegreeLabel!.Text = character.AvailableDegreeStats.ToString(CultureInfo.InvariantCulture);

        xLabel!.Text = character.X.ToString("F1", CultureInfo.InvariantCulture);
        yLabel!.Text = character.Y.ToString("F1", CultureInfo.InvariantCulture);
        zLabel!.Text = character.Z.ToString("F1", CultureInfo.InvariantCulture);
        angleLabel!.Text = character.Angle.ToString("F1", CultureInfo.InvariantCulture);

        suppressStatCallbacks = true;
        SetXpRow(titleXpBar!, titleXpEdit!, titleXpLabel!, character.TitleXP, xpToLevel);
        SetXpRow(degreeXpBar!, degreeXpEdit!, degreeXpLabel!, character.DegreeXP, xpToLevel);
        SetLevelEdit(titleLevelEdit!, character.TitleMinusOne);
        SetLevelEdit(degreeLevelEdit!, character.DegreeMinusOne);
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
        FillGuildDropdowns(female: false);
        SelectGuildDropdowns(Guild.None, 0, enabled: false);
        ColorGuildDropdowns(character: null);
        clanLabel!.Text = CharacterLocaleText.ClanLine(null, locale);
        EndKarmaEditDisplay();
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
        if (statConfirmRow is not null)
        {
            statConfirmRow.Visible = false;
        }
        xLabel!.Text = "<empty>";
        yLabel!.Text = "<empty>";
        zLabel!.Text = "<empty>";
        angleLabel!.Text = "<empty>";

        suppressStatCallbacks = true;
        SetXpRow(titleXpBar!, titleXpEdit!, titleXpLabel!, 0, 1);
        SetXpRow(degreeXpBar!, degreeXpEdit!, degreeXpLabel!, 0, 1);
        titleLevelEdit!.Text = string.Empty;
        titleLevelEdit.Editable = false;
        degreeLevelEdit!.Text = string.Empty;
        degreeLevelEdit.Editable = false;
        titleXpEdit!.Text = string.Empty;
        titleXpEdit.Editable = false;
        degreeXpEdit!.Text = string.Empty;
        degreeXpEdit.Editable = false;
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

    private void AddTierRow(Control parent, Texture2D? icon, bool isTitle, out Label name, out Label xpText,
        out ProgressBar bar)
    {
        var block = new VBoxContainer();
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        top.AddChild(MakeIconRect(icon));
        name = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var edit = new LineEdit
        {
            CustomMinimumSize = new Vector2(48, 0),
            Alignment = HorizontalAlignment.Center
        };
        var minus = new Button { Text = "−", CustomMinimumSize = new Vector2(28, 0) };
        var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(28, 0) };
        var xpEdit = new LineEdit
        {
            CustomMinimumSize = new Vector2(72, 0),
            Alignment = HorizontalAlignment.Right
        };
        xpText = new Label();
        edit.TextSubmitted += _ => CommitLevel(isTitle, edit);
        edit.FocusExited += () => CommitLevel(isTitle, edit);
        minus.Pressed += () => NudgeLevel(isTitle, -1);
        plus.Pressed += () => NudgeLevel(isTitle, 1);
        xpEdit.TextSubmitted += _ => CommitXp(isTitle, xpEdit);
        xpEdit.FocusExited += () => CommitXp(isTitle, xpEdit);
        top.AddChild(name);
        top.AddChild(edit);
        top.AddChild(minus);
        top.AddChild(plus);
        top.AddChild(xpEdit);
        top.AddChild(xpText);
        block.AddChild(top);
        bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, 8),
            ShowPercentage = false,
            MaxValue = 1,
            Value = 0
        };
        var fill = isTitle ? new Color(0.25f, 0.75f, 0.2f) : new Color(0.25f, 0.55f, 0.95f);
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        block.AddChild(bar);
        parent.AddChild(block);

        if (isTitle)
        {
            titleLevelEdit = edit;
            titleXpEdit = xpEdit;
        }
        else
        {
            degreeLevelEdit = edit;
            degreeXpEdit = xpEdit;
        }
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

    private static Label AddCenteredIconValue(Control parent, Texture2D? icon)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(MakeIconRect(icon));
        var label = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        row.AddChild(label);
        parent.AddChild(row);
        return label;
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

    private static void SetLevelEdit(LineEdit edit, int minusOne)
    {
        edit.Editable = true;
        if (!edit.HasFocus())
        {
            edit.Text = (minusOne + 1).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static int NudgeStoredLevel(int currentMinusOne, int delta)
    {
        var next = currentMinusOne + delta;
        if (next < 0 || next > CharacterDataHelper.MaxLevelMinusOne)
        {
            return currentMinusOne;
        }

        return next;
    }

    private void FillGuildDropdowns(bool female)
    {
        if (guildSelect is null || rankSelect is null || guildLabel is null)
        {
            return;
        }

        guildLabel.Text = CharacterLocaleText.GuildHeading(locale);
        if (guildDropdownsLocale == locale && guildDropdownsFemale == female
            && guildSelect.ItemCount > 0 && rankSelect.ItemCount > 0)
        {
            return;
        }

        var previousGuild = guildSelect.ItemCount > 0 ? (Guild)guildSelect.GetSelectedId() : Guild.None;
        var previousRank = rankSelect.ItemCount > 0 ? rankSelect.GetSelectedId() : 0;

        suppressStatCallbacks = true;
        guildSelect.Clear();
        guildSelect.AddItem("-", (int)Guild.None);
        foreach (var guild in GuildCatalog.LetterOrder)
        {
            var name = CharacterLocaleText.GuildName(guild, locale);
            var icon = AdminUiAtlas.GuildIcon(guild);
            if (icon is null)
            {
                guildSelect.AddItem(name, (int)guild);
            }
            else
            {
                guildSelect.AddIconItem(icon, name, (int)guild);
            }
        }

        rankSelect.Clear();
        for (var rank = 0; rank <= (int)GuildRank.Expert; rank++)
        {
            rankSelect.AddItem(CharacterLocaleText.GuildRankName(rank, female, locale), rank);
        }

        SelectGuildDropdowns(previousGuild, previousRank, enabled: selectedClientId is not null);
        suppressStatCallbacks = false;

        guildDropdownsLocale = locale;
        guildDropdownsFemale = female;
    }

    private void SelectGuildDropdowns(Guild guild, int rankMinusOne, bool enabled)
    {
        if (guildSelect is null || rankSelect is null)
        {
            return;
        }

        var wasSuppressing = suppressStatCallbacks;
        suppressStatCallbacks = true;
        var guildIndex = guildSelect.GetItemIndex((int)guild);
        guildSelect.Select(guildIndex >= 0 ? guildIndex : 0);
        var rankIndex = rankSelect.GetItemIndex(rankMinusOne);
        rankSelect.Select(rankIndex >= 0 ? rankIndex : 0);
        guildSelect.Disabled = !enabled;
        rankSelect.Disabled = !enabled || guild == Guild.None;
        suppressStatCallbacks = wasSuppressing;
    }

    private void ColorGuildDropdowns(CharacterDbEntry? character)
    {
        if (guildSelect is null || rankSelect is null)
        {
            return;
        }

        var title = character?.TitleMinusOne ?? 0;
        var degree = character?.DegreeMinusOne ?? 0;
        var selectedGuild = character?.Guild ?? Guild.None;
        var selectedRank = character is null || selectedGuild == Guild.None ? 0 : character.GuildLevelMinusOne;
        var currentUnmet = character is not null
                           && selectedGuild != Guild.None
                           && !GuildCatalog.MeetsRankRequirements(
                               selectedGuild, selectedRank, title, degree);

        for (var i = 0; i < guildSelect.ItemCount; i++)
        {
            var guild = (Guild)guildSelect.GetItemId(i);
            var met = guild == Guild.None
                      || GuildCatalog.MeetsRankRequirements(guild, selectedRank, title, degree);
            guildSelect.SetItemDisabled(i, character is not null && !met);
        }

        for (var i = 0; i < rankSelect.ItemCount; i++)
        {
            var rank = rankSelect.GetItemId(i);
            var met = selectedGuild == Guild.None
                      || GuildCatalog.MeetsRankRequirements(selectedGuild, rank, title, degree);
            rankSelect.SetItemDisabled(i, character is not null && !met);
        }

        SetOptionFontColor(guildSelect, currentUnmet);
        SetOptionFontColor(rankSelect, currentUnmet);
    }

    private static void SetOptionFontColor(OptionButton option, bool unmet)
    {
        if (unmet)
        {
            option.AddThemeColorOverride("font_color", GuildUnmetText);
            option.AddThemeColorOverride("font_hover_color", GuildUnmetText);
            option.AddThemeColorOverride("font_pressed_color", GuildUnmetText);
            option.AddThemeColorOverride("font_focus_color", GuildUnmetText);
            return;
        }

        option.RemoveThemeColorOverride("font_color");
        option.RemoveThemeColorOverride("font_hover_color");
        option.RemoveThemeColorOverride("font_pressed_color");
        option.RemoveThemeColorOverride("font_focus_color");
    }

    private void OnGuildSelected(long index)
    {
        if (suppressStatCallbacks || guildSelect is null || selectedClientId is null)
        {
            return;
        }

        var guild = (Guild)guildSelect.GetItemId((int)index);
        var character = GetSelectedClient()?.CurrentCharacter;
        if (character is null)
        {
            return;
        }

        var rank = rankSelect is null || guild == Guild.None ? 0 : rankSelect.GetSelectedId();
        if (guild != Guild.None
            && !GuildCatalog.MeetsRankRequirements(
                guild, rank, character.TitleMinusOne, character.DegreeMinusOne))
        {
            SelectGuildDropdowns(character.Guild, character.GuildLevelMinusOne, enabled: true);
            ColorGuildDropdowns(character);
            return;
        }

        AdminClientActions.SetGuild(selectedClientId.Value, guild, rank);
    }

    private void OnRankSelected(long index)
    {
        if (suppressStatCallbacks || guildSelect is null || rankSelect is null || selectedClientId is null)
        {
            return;
        }

        var guild = (Guild)guildSelect.GetSelectedId();
        var character = GetSelectedClient()?.CurrentCharacter;
        if (guild == Guild.None || character is null)
        {
            return;
        }

        var rank = rankSelect.GetItemId((int)index);
        if (!GuildCatalog.MeetsRankRequirements(
                guild, rank, character.TitleMinusOne, character.DegreeMinusOne))
        {
            SelectGuildDropdowns(character.Guild, character.GuildLevelMinusOne, enabled: true);
            ColorGuildDropdowns(character);
            return;
        }

        AdminClientActions.SetGuild(selectedClientId.Value, guild, rank);
    }

    private void OnKarmaLabelGuiInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            return;
        }

        BeginKarmaEdit();
        AcceptEvent();
    }

    private void BeginKarmaEdit()
    {
        if (karmaEditing || karmaEdit is null || karmaLabel is null)
        {
            return;
        }

        var character = GetSelectedClient()?.CurrentCharacter;
        if (character is null)
        {
            return;
        }

        karmaEditing = true;
        karmaEdit.Text = character.KarmaCount.ToString(CultureInfo.InvariantCulture);
        karmaLabel.Visible = false;
        karmaEdit.Visible = true;
        karmaEdit.GrabFocus();
        karmaEdit.SelectAll();
    }

    private void CommitKarmaEdit()
    {
        if (suppressStatCallbacks || !karmaEditing || karmaEdit is null)
        {
            return;
        }

        var text = karmaEdit.Text.Trim();
        EndKarmaEditDisplay();

        if (selectedClientId is null
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            Refresh();
            return;
        }

        AdminClientActions.SetKarmaCount(selectedClientId.Value, value);
        Refresh();
    }

    private void EndKarmaEditDisplay()
    {
        karmaEditing = false;
        if (karmaEdit is not null)
        {
            karmaEdit.Visible = false;
        }

        if (karmaLabel is not null)
        {
            karmaLabel.Visible = true;
        }
    }

    private void NudgeLevel(bool isTitle, int delta)
    {
        var client = GetSelectedClient();
        var character = client?.CurrentCharacter;
        if (client is null || character is null)
        {
            return;
        }

        var oldTitle = character.TitleMinusOne;
        var oldDegree = character.DegreeMinusOne;
        var newTitle = isTitle ? NudgeStoredLevel(oldTitle, delta) : oldTitle;
        var newDegree = isTitle ? oldDegree : NudgeStoredLevel(oldDegree, delta);
        ApplyLevelEdit(client, character, isTitle, oldTitle, oldDegree, newTitle, newDegree);
    }

    private void CommitLevel(bool isTitle, LineEdit edit)
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

        var oldTitle = character.TitleMinusOne;
        var oldDegree = character.DegreeMinusOne;
        var currentMinusOne = isTitle ? oldTitle : oldDegree;
        if (!int.TryParse(edit.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < 1)
        {
            edit.Text = (currentMinusOne + 1).ToString(CultureInfo.InvariantCulture);
            return;
        }

        var newMinusOne = Math.Clamp(value - 1, 0, CharacterDataHelper.MaxLevelMinusOne);
        if (newMinusOne == currentMinusOne)
        {
            edit.Text = (currentMinusOne + 1).ToString(CultureInfo.InvariantCulture);
            return;
        }

        var newTitle = isTitle ? newMinusOne : oldTitle;
        var newDegree = isTitle ? oldDegree : newMinusOne;
        ApplyLevelEdit(client, character, isTitle, oldTitle, oldDegree, newTitle, newDegree);
    }

    private void CommitXp(bool isTitle, LineEdit edit)
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

        var oldXp = isTitle ? character.TitleXP : character.DegreeXP;
        var oldTitle = character.TitleMinusOne;
        var oldDegree = character.DegreeMinusOne;
        if (!uint.TryParse(edit.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            edit.Text = oldXp.ToString(CultureInfo.InvariantCulture);
            return;
        }

        if (!character.ApplyExperience(isTitle, value))
        {
            edit.Text = oldXp.ToString(CultureInfo.InvariantCulture);
            return;
        }

        var newXp = isTitle ? character.TitleXP : character.DegreeXP;
        suppressStatCallbacks = true;
        edit.Text = newXp.ToString(CultureInfo.InvariantCulture);
        suppressStatCallbacks = false;

        NetworkedStatsUpdater.Update(character);
        client.SaveCharacter();
        var kind = isTitle ? "title" : "degree";
        var oldLevel = isTitle ? oldTitle : oldDegree;
        var newLevel = isTitle ? character.TitleMinusOne : character.DegreeMinusOne;
        AdminActionLog.Info(client,
            $"changed {kind} XP from {oldXp} to {value} " +
            $"(stored {newXp}, {kind} {oldLevel} -> {newLevel})");
        Refresh();
    }

    private void ApplyLevelEdit(SphereClient client, CharacterDbEntry character, bool isTitle,
        int oldTitle, int oldDegree, int newTitle, int newDegree)
    {
        if (!character.LevelUp(newTitle, newDegree))
        {
            return;
        }

        NetworkedStatsUpdater.Update(character);
        client.SaveCharacter();
        var oldMinusOne = isTitle ? oldTitle : oldDegree;
        var newMinusOne = isTitle ? character.TitleMinusOne : character.DegreeMinusOne;
        var kind = isTitle ? "title" : "degree";
        AdminActionLog.Info(client, $"changed {kind} from {CharacterLocaleText.DisplayLevel(oldMinusOne)} " +
            $"(rebirth {CharacterLocaleText.RebirthCount(oldMinusOne)}) to " +
            $"{CharacterLocaleText.DisplayLevel(newMinusOne)} " +
            $"(rebirth {CharacterLocaleText.RebirthCount(newMinusOne)})");
        Refresh();
    }

    private void BeginStatEditIfNeeded(CharacterDbEntry character)
    {
        if (statEditSnapshot is not null)
        {
            return;
        }

        statEditSnapshot = new StatEditSnapshot
        {
            BaseStrength = character.BaseStrength,
            BaseAgility = character.BaseAgility,
            BaseAccuracy = character.BaseAccuracy,
            BaseEndurance = character.BaseEndurance,
            BaseEarth = character.BaseEarth,
            BaseAir = character.BaseAir,
            BaseWater = character.BaseWater,
            BaseFire = character.BaseFire,
            AvailableTitleStats = character.AvailableTitleStats,
            AvailableDegreeStats = character.AvailableDegreeStats
        };
        if (statConfirmRow is not null)
        {
            statConfirmRow.Visible = true;
        }
    }

    private void SubmitStatEdits()
    {
        var client = GetSelectedClient();
        var character = client?.CurrentCharacter;
        if (client is null || character is null || statEditSnapshot is null)
        {
            ClearStatEditSession();
            return;
        }

        NetworkedStatsUpdater.Update(character);
        client.SaveCharacter();
        AdminActionLog.Info(client, "submitted stat edits");
        ClearStatEditSession();
        Refresh();
    }

    /// <summary>
    ///     Drop an unfinished stat-edit session without restoring the snapshot.
    ///     Used when something else (e.g. character reset) already overwrote the live stats.
    /// </summary>
    public void DiscardPendingStatEdits()
    {
        ClearStatEditSession();
    }

    private void CancelStatEdits()
    {
        var character = GetSelectedClient()?.CurrentCharacter;
        if (character is not null && statEditSnapshot is not null)
        {
            RestoreStatEdit(character, statEditSnapshot);
            var client = GetSelectedClient();
            if (client is not null)
            {
                AdminActionLog.Info(client, "cancelled stat edits");
            }
        }

        ClearStatEditSession();
        Refresh();
    }

    private void ClearStatEditSession()
    {
        statEditSnapshot = null;
        if (statConfirmRow is not null)
        {
            statConfirmRow.Visible = false;
        }
    }

    private static void RestoreStatEdit(CharacterDbEntry character, StatEditSnapshot snapshot)
    {
        character.BaseStrength = snapshot.BaseStrength;
        character.BaseAgility = snapshot.BaseAgility;
        character.BaseAccuracy = snapshot.BaseAccuracy;
        character.BaseEndurance = snapshot.BaseEndurance;
        character.BaseEarth = snapshot.BaseEarth;
        character.BaseAir = snapshot.BaseAir;
        character.BaseWater = snapshot.BaseWater;
        character.BaseFire = snapshot.BaseFire;
        character.AvailableTitleStats = snapshot.AvailableTitleStats;
        character.AvailableDegreeStats = snapshot.AvailableDegreeStats;
        character.RecalcCurrentStats();
    }

    private static TextureButton MakeAtlasButton(Texture2D? texture)
    {
        var size = texture?.GetSize() ?? new Vector2(31, 24);
        return new TextureButton
        {
            TextureNormal = texture,
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Keep,
            TextureFilter = TextureFilterEnum.Nearest,
            CustomMinimumSize = size,
            FocusMode = FocusModeEnum.None
        };
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
        BeginStatEditIfNeeded(character);
        if (!character.ApplyCurrentStatEdit(stat, oldValue + delta))
        {
            return;
        }

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
        if (value == oldValue)
        {
            return;
        }

        BeginStatEditIfNeeded(character);
        if (!character.ApplyCurrentStatEdit(stat, value))
        {
            return;
        }

        Refresh();
    }

    private SphereClient? GetSelectedClient() =>
        selectedClientId is null ? null : ActiveClients.Get(selectedClientId.Value);

    private static void SetXpRow(ProgressBar bar, LineEdit edit, Label maxLabel, double current, double max)
    {
        var safeMax = Math.Max(1.0, max);
        bar.MaxValue = safeMax;
        bar.Value = Math.Clamp(current, 0, safeMax);
        maxLabel.Text = $"/ {max}";
        edit.Editable = true;
        if (!edit.HasFocus())
        {
            edit.Text = current.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void SetBar(ProgressBar bar, Label label, double current, double max)
    {
        var safeMax = Math.Max(1.0, max);
        bar.MaxValue = safeMax;
        bar.Value = Math.Clamp(current, 0, safeMax);
        label.Text = $"{current} / {max}";
    }
}
