using System.Collections.Generic;
using Godot;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Persona sketch: custom gender background + slot overlay + item icons from
///     <see cref="CharacterDbEntry.Items"/>, mutator column, and inventory panel on the right.
///     Art is 256×370 under <c>ui_custom/</c>. Slot hit targets match overlay frame rects.
/// </summary>
public partial class PersonaPanel : PanelContainer
{
    private const float DesignWidth = 256f;
    private const float DesignHeight = 370f;
    private const float MaxWidth = 500f;
    private const float MutatorSize = 32f;
    private const float MutatorSeparation = 2f;

    private static readonly BelongingSlot[] MutatorSlotsTopToBottom =
    [
        BelongingSlot.Mutator_10,
        BelongingSlot.Mutator_9,
        BelongingSlot.Mutator_8,
        BelongingSlot.Mutator_7,
        BelongingSlot.Mutator_6,
        BelongingSlot.Mutator_5,
        BelongingSlot.Mutator_4,
        BelongingSlot.Mutator_3,
        BelongingSlot.Mutator_2,
        BelongingSlot.Mutator_1
    ];

    /// <summary>Exact outer slot-frame rects on slot_overlay.png (left, top, width, height).</summary>
    private static readonly (Rect2I Rect, BelongingSlot Slot)[] SlotLayout =
    [
        // Far left — special
        (new(15, 40, 33, 32), BelongingSlot.Special_1),
        (new(15, 75, 33, 32), BelongingSlot.Special_2),
        (new(15, 110, 33, 32), BelongingSlot.Special_3),
        (new(15, 145, 33, 32), BelongingSlot.Special_4),
        (new(15, 180, 33, 32), BelongingSlot.Special_5),
        (new(15, 215, 33, 32), BelongingSlot.Special_6),
        (new(15, 250, 33, 32), BelongingSlot.Special_7),
        (new(15, 285, 33, 32), BelongingSlot.Special_8),
        (new(15, 320, 33, 32), BelongingSlot.Special_9),

        // Inner left
        (new(64, 40, 33, 32), BelongingSlot.Helmet),
        (new(64, 75, 33, 32), BelongingSlot.Amulet),
        (new(63, 145, 33, 32), BelongingSlot.Shield),
        (new(63, 180, 33, 32), BelongingSlot.BraceletLeft),
        (new(63, 215, 33, 32), BelongingSlot.Ring_1),
        (new(63, 250, 33, 32), BelongingSlot.Ring_2),

        // Center (armor)
        (new(107, 113, 33, 32), BelongingSlot.Chestplate),
        (new(107, 148, 33, 32), BelongingSlot.Belt),
        (new(107, 182, 33, 32), BelongingSlot.Pants),
        (new(107, 284, 33, 32), BelongingSlot.Boots),

        // Inner right
        (new(153, 73, 33, 32), BelongingSlot.Guild),
        (new(152, 148, 33, 32), BelongingSlot.Gloves),
        (new(152, 182, 33, 32), BelongingSlot.BraceletRight),
        (new(152, 216, 33, 32), BelongingSlot.Ring_3),
        (new(152, 250, 33, 32), BelongingSlot.Ring_4),

        // Far right
        (new(199, 58, 33, 32), BelongingSlot.MainHand),
        (new(199, 92, 33, 32), BelongingSlot.Ammo),
        (new(199, 148, 33, 32), BelongingSlot.MapBook),
        (new(199, 182, 33, 32), BelongingSlot.RecipeBook),
        (new(199, 216, 33, 32), BelongingSlot.MantraBook),
        (new(199, 250, 33, 32), BelongingSlot.Inkpot),
        (new(199, 284, 33, 32), BelongingSlot.TokenIsland),
        (new(199, 318, 33, 32), BelongingSlot.SpeedhackMantra),
    ];

    private ushort? selectedClientId;
    private TextureRect? background;
    private ItemDetailsPopupHost? popupHost;
    private InventoryPanel? inventoryPanel;
    private AdminSlotItemTools? itemTools;
    private readonly Dictionary<BelongingSlot, Control> slotPanels = new();
    private readonly Dictionary<BelongingSlot, TextureRect> slotIcons = new();
    private readonly Dictionary<BelongingSlot, ColorRect> slotUnmetBgs = new();
    private readonly Dictionary<BelongingSlot, int?> lastItemIds = new();
    private readonly Dictionary<BelongingSlot, bool> lastUnmet = new();

    private static readonly Color UnmetSlotBg = new(0.72f, 0.08f, 0.08f, 0.55f);

    public void SetPopupHost(ItemDetailsPopupHost host)
    {
        popupHost = host;
        inventoryPanel?.SetPopupHost(host);
        foreach (var hit in slotPanels.Values)
        {
            if (hit is AdminItemSlot slot)
            {
                slot.PopupHost = host;
            }
        }
    }

    public void SetItemTools(AdminSlotItemTools tools)
    {
        itemTools = tools;
        inventoryPanel?.SetItemTools(tools);
    }

    public override void _Ready()
    {
        var maxHeight = MaxWidth * DesignHeight / DesignWidth;
        // Persona + mutators + inventory panel (design 215 scaled to MaxWidth-equivalent).
        var inventoryWidth = MaxWidth * (215f / 256f);
        CustomMinimumSize = new Vector2(MaxWidth + MutatorSize + 8f + inventoryWidth + 8f, maxHeight);
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        ClipContents = true;

        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.07f, 0.07f, 0.94f),
            BorderColor = new Color(0.45f, 0.36f, 0.22f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 4,
            ContentMarginTop = 4,
            ContentMarginRight = 4,
            ContentMarginBottom = 4
        });

        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        row.AddThemeConstantOverride("separation", 4);
        AddChild(row);

        var aspect = new AspectRatioContainer
        {
            Ratio = DesignWidth / DesignHeight,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            StretchMode = AspectRatioContainer.StretchModeEnum.Fit,
            AlignmentHorizontal = AspectRatioContainer.AlignmentMode.Center,
            AlignmentVertical = AspectRatioContainer.AlignmentMode.Center
        };
        row.AddChild(aspect);

        // AspectRatioContainer assigns this child's size — do not FullRect it.
        var frame = new Control { MouseFilter = MouseFilterEnum.Ignore };
        aspect.AddChild(frame);

        background = new TextureRect
        {
            Texture = AdminUiAtlas.PersonaMaleBackground,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(background);

        var slotOverlay = new TextureRect
        {
            Texture = AdminUiAtlas.PersonaSlotOverlay,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore
        };
        slotOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(slotOverlay);

        foreach (var (rect, slot) in SlotLayout)
        {
            var (hit, icon, unmetBg) = CreateOverlaySlot(rect, slot);
            frame.AddChild(hit);
            slotPanels[slot] = hit;
            slotIcons[slot] = icon;
            slotUnmetBgs[slot] = unmetBg;
        }

        var mutatorColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };
        mutatorColumn.AddThemeConstantOverride("separation", (int)MutatorSeparation);
        row.AddChild(mutatorColumn);

        foreach (var slot in MutatorSlotsTopToBottom)
        {
            var (hit, icon, unmetBg) = CreateMutatorSlot(slot);
            mutatorColumn.AddChild(hit);
            slotPanels[slot] = hit;
            slotIcons[slot] = icon;
            slotUnmetBgs[slot] = unmetBg;
        }

        // Inventory sits immediately right of mutators; drop its own outer chrome so we keep one frame.
        inventoryPanel = new InventoryPanel { Embedded = true };
        row.AddChild(inventoryPanel);
        if (popupHost is not null)
        {
            inventoryPanel.SetPopupHost(popupHost);
        }

        ClientStateEvents.CharacterChanged += OnCharacterChanged;
        ClientStateEvents.RosterChanged += OnRosterChanged;
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
            lastItemIds.Clear();
            lastUnmet.Clear();
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

    private (Control Hit, TextureRect Icon, ColorRect UnmetBg) CreateOverlaySlot(Rect2I rect, BelongingSlot slot)
    {
        var hit = new AdminItemSlot
        {
            Name = $"Slot_{slot}",
            Slot = slot,
            GetClientId = () => selectedClientId,
            PopupHost = popupHost,
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.None
        };
        hit.SetMeta("BelongingSlot", (int)slot);
        hit.AnchorLeft = rect.Position.X / DesignWidth;
        hit.AnchorTop = rect.Position.Y / DesignHeight;
        hit.AnchorRight = (rect.Position.X + rect.Size.X) / DesignWidth;
        hit.AnchorBottom = (rect.Position.Y + rect.Size.Y) / DesignHeight;
        hit.OffsetLeft = 0;
        hit.OffsetTop = 0;
        hit.OffsetRight = 0;
        hit.OffsetBottom = 0;

        var unmetBg = CreateUnmetBackground();
        hit.AddChild(unmetBg);
        var icon = CreateSlotIcon(null);
        hit.AddChild(icon);
        hit.Icon = icon;
        WireSlotInput(hit, slot);
        return (hit, icon, unmetBg);
    }

    private (Control Hit, TextureRect Icon, ColorRect UnmetBg) CreateMutatorSlot(BelongingSlot slot)
    {
        var hit = new AdminItemSlot
        {
            Name = $"Slot_{slot}",
            Slot = slot,
            GetClientId = () => selectedClientId,
            PopupHost = popupHost,
            CustomMinimumSize = new Vector2(MutatorSize, MutatorSize),
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.None
        };
        hit.SetMeta("BelongingSlot", (int)slot);

        var unmetBg = CreateUnmetBackground();
        hit.AddChild(unmetBg);
        var icon = CreateSlotIcon(AdminUiAtlas.MutatorPlaceholder);
        hit.AddChild(icon);
        hit.Icon = icon;
        WireSlotInput(hit, slot);
        return (hit, icon, unmetBg);
    }

    private static ColorRect CreateUnmetBackground() =>
        new()
        {
            Color = UnmetSlotBg,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorsPreset = (int)LayoutPreset.FullRect,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 1,
            OffsetTop = 1,
            OffsetRight = -1,
            OffsetBottom = -1
        };

    private void WireSlotInput(Control hit, BelongingSlot slot)
    {
        hit.MouseEntered += () => OnSlotMouseEntered(hit, slot);
        hit.MouseExited += () => OnSlotMouseExited(slot);
        hit.GuiInput += inputEvent => OnSlotGuiInput(hit, slot, inputEvent);
        if (hit is AdminItemSlot adminSlot)
        {
            adminSlot.ContextRequested += OnSlotContextRequested;
        }
    }

    private void OnSlotContextRequested(BelongingSlot slot, Vector2 globalPos)
    {
        if (selectedClientId is null || itemTools is null)
        {
            return;
        }

        itemTools.OpenMenu(selectedClientId.Value, slot, globalPos);
    }

    private void OnSlotMouseEntered(Control hit, BelongingSlot slot)
    {
        if (GetViewport().GuiIsDragging()
            || popupHost is null
            || !TryGetSlotItemId(slot, out var itemId))
        {
            return;
        }

        var character = selectedClientId is null
            ? null
            : ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter;
        popupHost.ShowHover(hit.GetGlobalRect().Position, itemId, slot, character);
    }

    private void OnSlotMouseExited(BelongingSlot slot)
    {
        popupHost?.HideHover(slot);
    }

    private void OnSlotGuiInput(Control hit, BelongingSlot slot, InputEvent inputEvent)
    {
        if (popupHost is null)
        {
            return;
        }

        if (inputEvent is not InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
                ShiftPressed: true
            })
        {
            return;
        }

        if (!TryGetSlotItemId(slot, out var itemId))
        {
            return;
        }

        var character = selectedClientId is null
            ? null
            : ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter;
        popupHost.Pin(hit.GetGlobalRect().Position, itemId, slot, character);
        hit.AcceptEvent();
    }

    private bool TryGetSlotItemId(BelongingSlot slot, out int itemId)
    {
        itemId = 0;
        var character = selectedClientId is null
            ? null
            : ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter;
        return character is not null && character.Items.TryGetValue(slot, out itemId);
    }

    private static TextureRect CreateSlotIcon(Texture2D? texture) =>
        new()
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorsPreset = (int)LayoutPreset.FullRect,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 1,
            OffsetTop = 1,
            OffsetRight = -1,
            OffsetBottom = -1
        };

    public Control? GetSlotPanel(BelongingSlot slot) =>
        slotPanels.TryGetValue(slot, out var panel) ? panel : null;

    public void SetSelectedClient(ushort? clientId)
    {
        selectedClientId = clientId;
        lastItemIds.Clear();
        lastUnmet.Clear();
        RequestRefresh();
        inventoryPanel?.SetSelectedClient(clientId);
    }

    private void Refresh()
    {
        if (background is null)
        {
            return;
        }

        var character = selectedClientId is null
            ? null
            : ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter;

        background.Texture = character is { IsGenderFemale: true }
            ? AdminUiAtlas.PersonaFemaleBackground
            : AdminUiAtlas.PersonaMaleBackground;

        RefreshSlotIcons(character);
    }

    private void RefreshSlotIcons(CharacterDbEntry? character)
    {
        foreach (var (slot, icon) in slotIcons)
        {
            int? itemId = null;
            if (character is not null && character.Items.TryGetValue(slot, out var id))
            {
                itemId = id;
            }

            var unmet = false;
            Texture2D? texture;
            if (itemId is null)
            {
                texture = ResolveSlotTexture(slot, null);
            }
            else
            {
                var item = DbConnection.Items.FindById(itemId.Value);
                texture = item is null
                    ? ResolveSlotTexture(slot, null)
                    : AdminUiAtlas.ItemIcon(item.ModelNameInventory)
                      ?? ResolveSlotTexture(slot, null);
                unmet = item is not null && character is not null && !character.CanUseItem(item);
            }

            if (lastItemIds.TryGetValue(slot, out var previous)
                && previous == itemId
                && lastUnmet.TryGetValue(slot, out var prevUnmet)
                && prevUnmet == unmet)
            {
                continue;
            }

            lastItemIds[slot] = itemId;
            lastUnmet[slot] = unmet;
            icon.Texture = texture;
            if (slotUnmetBgs.TryGetValue(slot, out var bg))
            {
                bg.Visible = unmet;
            }
        }
    }

    private static Texture2D? ResolveSlotTexture(BelongingSlot slot, int? itemId)
    {
        var isMutator = slot is >= BelongingSlot.Mutator_1 and <= BelongingSlot.Mutator_10;
        if (itemId is null)
        {
            return isMutator ? AdminUiAtlas.MutatorPlaceholder : null;
        }

        var item = DbConnection.Items.FindById(itemId.Value);
        if (item is null)
        {
            return isMutator ? AdminUiAtlas.MutatorPlaceholder : null;
        }

        return AdminUiAtlas.ItemIcon(item.ModelNameInventory)
               ?? (isMutator ? AdminUiAtlas.MutatorPlaceholder : null);
    }
}
