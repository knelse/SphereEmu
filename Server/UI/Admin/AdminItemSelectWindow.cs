using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer.Server.UI.Localization;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Pick a catalog item (type → name → suffix) and apply it to an admin slot.
///     Column 4 is the same item description used by hover/pin popups.
/// </summary>
public partial class AdminItemSelectWindow : Window
{
    private ItemList? typeList;
    private ItemList? guildList;
    private ItemList? nameList;
    private ItemList? suffixList;
    private Control? guildColumn;
    private Control? suffixColumn;
    private VBoxContainer? previewBox;
    private Button? applyButton;
    private PanelContainer? toastPanel;
    private Label? toastLabel;
    private Tween? toastTween;
    private Locale locale = Locale.Russian;
    private ushort? targetClientId;
    private BelongingSlot targetSlot;
    private int? preselectGameId;
    private ItemSuffix preselectSuffix = ItemSuffix.None;

    private readonly List<CatalogGroup> types = [];
    private readonly List<Guild> guildFilters = [];
    private readonly List<SphGameObject> names = [];
    private readonly List<ItemSuffix> suffixes = [];

    private static readonly Color UnmetText = new(0.95f, 0.28f, 0.28f);

    private static Dictionary<CatalogGroup, List<SphGameObject>>? catalogByGroup;

    public void SetLocale(Locale newLocale)
    {
        locale = newLocale;
        if (Visible)
        {
            RefreshTypes(keepSelection: true);
        }
    }

    public void OpenFor(ushort clientId, BelongingSlot slot)
    {
        targetClientId = clientId;
        targetSlot = slot;
        Title = $"Change item — {slot}";
        CapturePreselect(clientId, slot);
        if (!Visible)
        {
            PopupCentered();
        }

        RefreshTypes(keepSelection: false);
        ApplyPreselect();
    }

    public override void _Ready()
    {
        Title = "Change item";
        Size = new Vector2I(1180, 560);
        Unresizable = false;
        Exclusive = false;
        Transient = true;
        Visible = false;
        CloseRequested += Hide;

        var root = new MarginContainer();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("margin_left", 10);
        root.AddThemeConstantOverride("margin_top", 10);
        root.AddThemeConstantOverride("margin_right", 10);
        root.AddThemeConstantOverride("margin_bottom", 10);
        AddChild(root);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        root.AddChild(body);

        var splits = new HSplitContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            DraggerVisibility = SplitContainer.DraggerVisibilityEnum.Visible
        };
        body.AddChild(splits);

        typeList = MakeLabeledList(splits, "Type", stretch: 1f, out _);
        guildList = MakeLabeledList(splits, "Guild", stretch: 1f, out guildColumn);
        nameList = MakeLabeledList(splits, "Item", stretch: 1.2f, out _);
        suffixList = MakeLabeledList(splits, "Suffix", stretch: 1f, out suffixColumn);
        previewBox = MakePreviewColumn(splits);
        guildColumn.Visible = false;

        typeList.ItemSelected += OnTypeSelected;
        guildList.ItemSelected += OnGuildSelected;
        nameList.ItemSelected += OnNameSelected;
        suffixList.ItemSelected += OnSuffixSelected;

        var buttons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        buttons.AddThemeConstantOverride("separation", 8);
        body.AddChild(buttons);

        toastPanel = new PanelContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        toastPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.14f, 0.11f, 0.09f, 0.96f),
            BorderColor = new Color(0.55f, 0.42f, 0.25f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 10,
            ContentMarginTop = 4,
            ContentMarginRight = 10,
            ContentMarginBottom = 4
        });
        toastLabel = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        toastLabel.AddThemeColorOverride("font_color", UnmetText);
        toastPanel.AddChild(toastLabel);
        buttons.AddChild(toastPanel);

        applyButton = new Button { Text = "Apply", Disabled = true };
        applyButton.Pressed += TryApply;
        buttons.AddChild(applyButton);

        var closeButton = new Button { Text = "Close" };
        closeButton.Pressed += Hide;
        buttons.AddChild(closeButton);
    }

    private static ItemList MakeLabeledList(Control parent, string title, float stretch, out Control column)
    {
        column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = stretch,
            CustomMinimumSize = new Vector2(80, 0)
        };
        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);
        column.AddChild(new Label { Text = title });
        var list = new ItemList
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = true
        };
        column.AddChild(list);
        return list;
    }

    private static VBoxContainer MakePreviewColumn(Control parent)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(80, 0),
            SizeFlagsStretchRatio = 1.35f
        };
        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);
        column.AddChild(new Label { Text = "Preview" });
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        column.AddChild(scroll);
        var box = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        box.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(box);
        return box;
    }

    private void CapturePreselect(ushort clientId, BelongingSlot slot)
    {
        preselectGameId = null;
        preselectSuffix = ItemSuffix.None;
        var character = ActiveClients.Get(clientId)?.CurrentCharacter;
        if (character is null || !character.Items.TryGetValue(slot, out var itemId))
        {
            return;
        }

        var item = SphServer.Shared.Db.DbConnection.Items.FindById(itemId);
        if (item is null)
        {
            return;
        }

        preselectGameId = item.GameId;
        preselectSuffix = item.Suffix;
    }

    private void RefreshTypes(bool keepSelection)
    {
        if (typeList is null)
        {
            return;
        }

        var selectedGroup = keepSelection ? SelectedGroup() : null;
        EnsureCatalog();
        types.Clear();
        typeList.Clear();
        foreach (var group in catalogByGroup!.Keys.OrderBy(g => g.Label(locale),
                     StringComparer.CurrentCultureIgnoreCase))
        {
            if (!GroupAllowedForTarget(group))
            {
                continue;
            }

            types.Add(group);
            typeList.AddItem(group.Label(locale));
        }

        if (selectedGroup is { } previous)
        {
            var index = types.IndexOf(previous);
            if (index >= 0)
            {
                typeList.Select(index);
            }
            else if (types.Count > 0)
            {
                typeList.Select(0);
            }
        }
        else if (types.Count > 0)
        {
            typeList.Select(0);
        }

        UpdateGuildSuffixColumns();
        RefreshNames();
    }

    private void ApplyPreselect()
    {
        if (typeList is null || preselectGameId is not { } gameId
            || !SphObjectDb.GameObjectDataDb.TryGetValue(gameId, out var go))
        {
            RefreshNames();
            return;
        }

        if (SelectedGroup() is { IsGuilds: true } && go.RequiredGuild is not Guild.None)
        {
            var guildIndex = guildFilters.IndexOf(go.RequiredGuild);
            if (guildIndex >= 0 && guildList is not null)
            {
                guildList.Select(guildIndex);
            }
        }

        RefreshNames();
        var nameIndex = names.FindIndex(n => n.GameId == gameId);
        if (nameIndex >= 0 && nameList is not null)
        {
            nameList.Select(nameIndex);
        }

        RefreshSuffixes();
        var suffixIndex = suffixes.IndexOf(preselectSuffix);
        if (suffixIndex >= 0 && suffixList is not null)
        {
            suffixList.Select(suffixIndex);
        }

        RefreshPreview();
    }

    private void OnTypeSelected(long _)
    {
        UpdateGuildSuffixColumns();
        RefreshNames();
    }

    private void OnGuildSelected(long _)
    {
        RefreshNames();
    }

    private void OnNameSelected(long _)
    {
        RefreshSuffixes();
        RefreshPreview();
    }

    private void OnSuffixSelected(long _)
    {
        RefreshPreview();
    }

    private void RefreshNames()
    {
        if (nameList is null)
        {
            return;
        }

        EnsureCatalog();
        names.Clear();
        nameList.Clear();
        var group = SelectedGroup();
        if (group is { } selected && catalogByGroup!.TryGetValue(selected, out var gos))
        {
            var allowed = gos.Where(AllowedInTargetSlot);
            if (selected.IsGuilds && SelectedFilterGuild() is { } guild)
            {
                allowed = allowed.Where(go =>
                    go.RequiredGuild == guild || go.RequiredGuild is Guild.None);
            }

            var labeled = allowed
                .OrderBy(go => MissesReqs(go, ItemSuffix.None))
                .ThenBy(go => go.GameId)
                .Select(go => (Label: ItemLabel(go), Go: go))
                .ToList();
            foreach (var (label, go) in labeled)
            {
                names.Add(go);
                nameList.AddItem(label);
                if (MissesReqs(go, ItemSuffix.None))
                {
                    nameList.SetItemCustomFgColor(names.Count - 1, UnmetText);
                }
            }
        }

        RefreshSuffixes();
        RefreshPreview();
    }

    private void RefreshSuffixes()
    {
        if (suffixList is null)
        {
            return;
        }

        suffixes.Clear();
        suffixList.Clear();
        suffixes.Add(ItemSuffix.None);
        suffixList.AddItem("-");
        var go = SelectedGameObject();
        if (go is not null && MissesReqs(go, ItemSuffix.None))
        {
            suffixList.SetItemCustomFgColor(0, UnmetText);
        }

        var group = SelectedGroup();
        if (group is not { IsGuilds: true }
            && go is { } selectedGo
            && selectedGo.IsTierVisible()
            && HasSuffixSet(selectedGo)
            && GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual.TryGetValue(
                selectedGo.GameObjectType, out var map))
        {
            var prefType = SphObjectDbHelper.GameObjectToPrefTypeMap.GetValueOrDefault(
                selectedGo.GameObjectType, GameObjectType.Unknown);
            foreach (var (suffix, entry) in map.OrderBy(kv => kv.Value.value))
            {
                if (prefType is GameObjectType.Unknown
                    || !SphObjectDb.SuffixDataDb.TryGetValue(prefType, out var prefs)
                    || !prefs.ContainsKey(suffix))
                {
                    continue;
                }

                suffixes.Add(suffix);
                var name = ItemLocaleText.SuffixName(selectedGo.GameObjectType, suffix, locale)
                           ?? suffix.ToString();
                suffixList.AddItem($"{name} [{entry.value}]");
                if (MissesReqs(selectedGo, suffix))
                {
                    suffixList.SetItemCustomFgColor(suffixes.Count - 1, UnmetText);
                }
            }
        }

        suffixList.Select(0);
        UpdateApplyEnabled();
    }

    private void RefreshPreview()
    {
        if (previewBox is null)
        {
            return;
        }

        foreach (var child in previewBox.GetChildren())
        {
            child.QueueFree();
        }

        var item = BuildPreviewItem();
        if (item is null)
        {
            UpdateApplyEnabled();
            return;
        }

        var character = targetClientId is null
            ? null
            : ActiveClients.Get(targetClientId.Value)?.CurrentCharacter;
        AdminUiItemDetails.Fill(previewBox, item, character, locale);
        UpdateApplyEnabled();
    }

    private ItemDbEntry? BuildPreviewItem()
    {
        var go = SelectedGameObject();
        if (go is null)
        {
            return null;
        }

        return BuildItem(go, SelectedSuffix());
    }

    private bool MissesReqs(SphGameObject go, ItemSuffix suffix)
    {
        var character = targetClientId is null
            ? null
            : ActiveClients.Get(targetClientId.Value)?.CurrentCharacter;
        if (character is null)
        {
            return false;
        }

        var item = BuildItem(go, suffix);
        return item is not null && !character.CanUseItem(item);
    }

    private static ItemDbEntry? BuildItem(SphGameObject go, ItemSuffix suffix)
    {
        var clone = SphGameObject.CreateFromGameObject(go);
        clone.Suffix = suffix;
        return ItemDbEntry.CreateFromGameObject(clone);
    }

    private void TryApply()
    {
        if (targetClientId is null || SelectedGameObject() is not { } go)
        {
            return;
        }

        if (!SelectionCanBeEquipped())
        {
            ShowToast("Requirements unmet");
            return;
        }

        HideToast();
        AdminClientActions.ReplaceSlotItem(targetClientId.Value, targetSlot, go.GameId, SelectedSuffix());
    }

    private void UpdateApplyEnabled()
    {
        HideToast();
        if (applyButton is not null)
        {
            applyButton.Disabled = targetClientId is null || SelectedGameObject() is null;
        }
    }

    private bool SelectionCanBeEquipped()
    {
        if (targetClientId is null || SelectedGameObject() is not { } go)
        {
            return false;
        }

        var character = ActiveClients.Get(targetClientId.Value)?.CurrentCharacter;
        if (character is null)
        {
            return false;
        }

        var item = BuildItem(go, SelectedSuffix());
        if (item is null || !ItemDbEntry.IsAllowedInSlot(go.GameObjectType, go.ObjectKind, targetSlot))
        {
            return false;
        }

        return ItemDbEntry.IsInventorySlot(targetSlot) || character.CanUseItem(item);
    }

    private void ShowToast(string text)
    {
        if (toastPanel is null || toastLabel is null)
        {
            return;
        }

        toastLabel.Text = text;
        toastPanel.Modulate = Colors.White;
        toastPanel.Visible = true;
        toastTween?.Kill();
        toastTween = CreateTween();
        toastTween.TweenInterval(1.6);
        toastTween.TweenProperty(toastPanel, "modulate:a", 0f, 0.35);
        toastTween.TweenCallback(Callable.From(HideToast));
    }

    private void HideToast()
    {
        toastTween?.Kill();
        toastTween = null;
        if (toastPanel is not null)
        {
            toastPanel.Visible = false;
            toastPanel.Modulate = Colors.White;
        }
    }

    private bool GroupAllowedForTarget(CatalogGroup group)
    {
        if (!ItemDbEntry.HasSlotTypeFilter(targetSlot))
        {
            return true;
        }

        return catalogByGroup!.TryGetValue(group, out var gos) && gos.Any(AllowedInTargetSlot);
    }

    private bool AllowedInTargetSlot(SphGameObject go)
    {
        if (!ItemDbEntry.HasSlotTypeFilter(targetSlot))
        {
            return true;
        }

        return ItemDbEntry.IsAllowedInSlot(go.GameObjectType, go.ObjectKind, targetSlot);
    }

    private void UpdateGuildSuffixColumns()
    {
        var guildsMode = SelectedGroup() is { IsGuilds: true };
        if (guildColumn is not null)
        {
            guildColumn.Visible = guildsMode;
        }

        if (suffixColumn is not null)
        {
            suffixColumn.Visible = !guildsMode;
        }

        if (guildsMode)
        {
            RefreshGuildFilters();
        }
    }

    private void RefreshGuildFilters()
    {
        if (guildList is null)
        {
            return;
        }

        var previous = SelectedFilterGuild();
        guildFilters.Clear();
        guildList.Clear();
        var present = GuildsPresentForSlot();
        var characterGuild = targetClientId is null
            ? Guild.None
            : ActiveClients.Get(targetClientId.Value)?.CurrentCharacter?.Guild ?? Guild.None;
        foreach (var guild in GuildCatalog.LetterOrder)
        {
            if (!present.Contains(guild))
            {
                continue;
            }

            guildFilters.Add(guild);
            guildList.AddItem(CharacterLocaleText.GuildName(guild, locale));
            if (characterGuild != guild)
            {
                guildList.SetItemCustomFgColor(guildFilters.Count - 1, UnmetText);
            }
        }

        if (previous is { } keep)
        {
            var index = guildFilters.IndexOf(keep);
            guildList.Select(index >= 0 ? index : 0);
        }
        else if (guildFilters.Count > 0)
        {
            guildList.Select(0);
        }
    }

    private HashSet<Guild> GuildsPresentForSlot()
    {
        EnsureCatalog();
        var present = new HashSet<Guild>();
        if (catalogByGroup is null || !catalogByGroup.TryGetValue(CatalogGroup.Guilds, out var gos))
        {
            return present;
        }

        foreach (var go in gos)
        {
            if (go.RequiredGuild is Guild.None || !AllowedInTargetSlot(go))
            {
                continue;
            }

            present.Add(go.RequiredGuild);
        }

        return present;
    }

    private Guild? SelectedFilterGuild()
    {
        if (guildList is null || SelectedGroup() is not { IsGuilds: true })
        {
            return null;
        }

        var index = SelectedIndex(guildList);
        return index >= 0 && index < guildFilters.Count ? guildFilters[index] : null;
    }

    private static bool HasSuffixSet(SphGameObject go)
    {
        var set = go.SuffixSetName;
        return !string.IsNullOrWhiteSpace(set) && set.Length == 1 && set != "-";
    }

    private CatalogGroup? SelectedGroup()
    {
        if (typeList is null)
        {
            return null;
        }

        var index = SelectedIndex(typeList);
        return index >= 0 && index < types.Count ? types[index] : null;
    }

    private SphGameObject? SelectedGameObject()
    {
        if (nameList is null)
        {
            return null;
        }

        var index = SelectedIndex(nameList);
        return index >= 0 && index < names.Count ? names[index] : null;
    }

    private ItemSuffix SelectedSuffix()
    {
        if (suffixList is null)
        {
            return ItemSuffix.None;
        }

        var index = SelectedIndex(suffixList);
        return index >= 0 && index < suffixes.Count ? suffixes[index] : ItemSuffix.None;
    }

    private static int SelectedIndex(ItemList list)
    {
        var selected = list.GetSelectedItems();
        return selected.Length > 0 ? selected[0] : -1;
    }

    private string ItemLabel(SphGameObject go)
    {
        var name = ItemLocaleText.CatalogName(go, locale);
        var roman = RomanTier(go.Tier);
        return string.IsNullOrEmpty(roman) ? name : $"{name}  {roman}";
    }

    private static string RomanTier(int tier) => tier switch
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

    private static void EnsureCatalog()
    {
        if (catalogByGroup is not null)
        {
            return;
        }

        catalogByGroup = new Dictionary<CatalogGroup, List<SphGameObject>>();
        foreach (var go in SphObjectDb.GameObjectDataDb.Values)
        {
            var typeName = Enum.GetName(go.GameObjectType);
            if (typeName is null || typeName.StartsWith("Pref_", StringComparison.Ordinal))
            {
                continue;
            }

            var group = CatalogGroup.For(go);
            if (!catalogByGroup.TryGetValue(group, out var list))
            {
                list = [];
                catalogByGroup[group] = list;
            }

            list.Add(go);
        }
    }

    private readonly record struct CatalogGroup(bool IsGuilds, GameObjectType Type)
    {
        public static CatalogGroup Guilds { get; } = new(true, default);

        public static CatalogGroup For(SphGameObject go) =>
            go.ObjectKind == GameObjectKind.Guild ? Guilds : new(false, go.GameObjectType);

        public string Label(Locale locale) =>
            IsGuilds ? "Guilds" : ItemLocaleText.GameObjectTypeName(Type, locale);
    }
}
