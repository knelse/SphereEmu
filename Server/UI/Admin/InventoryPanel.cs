using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Inventory sketch: <c>ui_custom/inventory.png</c> (215×179) with 5×3 slot panels.
///     Top row: money / backpack / keys / mission; lower rows: Inventory_1..10.
/// </summary>
public partial class InventoryPanel : PanelContainer
{
    private const float DesignWidth = 215f;
    private const float DesignHeight = 179f;
    private const float MaxWidth = 420f;

    // Inner rect of the money text field on inventory.png (design space).
    private const float MoneyFieldLeft = 54f;
    private const float MoneyFieldTop = 149f;
    private const float MoneyFieldRight = 160f;
    private const float MoneyFieldBottom = 158f;

    private static readonly Color MoneyTextColor = new(0.92f, 0.88f, 0.7f);

    /// <summary>When true, omit outer chrome (used inside <see cref="PersonaPanel"/>).</summary>
    public bool Embedded { get; set; }

    /// <summary>Exact slot border rects on inventory.png (left, top, right exclusive, bottom exclusive).</summary>
    private static readonly (Rect2I Rect, BelongingSlot Slot)[] SlotLayout =
    [
        // Top row — special / tools (33×32)
        (new(19, 35, 33, 32), BelongingSlot.Money),
        (new(55, 35, 33, 32), BelongingSlot.Backpack),
        (new(91, 35, 33, 32), BelongingSlot.Key_1),
        (new(127, 35, 33, 32), BelongingSlot.Key_2),
        (new(163, 35, 33, 32), BelongingSlot.Mission),

        // Middle row — inventory 1–5 (34×33)
        (new(18, 69, 34, 33), BelongingSlot.Inventory_1),
        (new(54, 69, 34, 33), BelongingSlot.Inventory_2),
        (new(90, 69, 34, 33), BelongingSlot.Inventory_3),
        (new(126, 69, 34, 33), BelongingSlot.Inventory_4),
        (new(162, 69, 34, 33), BelongingSlot.Inventory_5),

        // Bottom row — inventory 6–10 (34×33)
        (new(18, 104, 34, 33), BelongingSlot.Inventory_6),
        (new(54, 104, 34, 33), BelongingSlot.Inventory_7),
        (new(90, 104, 34, 33), BelongingSlot.Inventory_8),
        (new(126, 104, 34, 33), BelongingSlot.Inventory_9),
        (new(162, 104, 34, 33), BelongingSlot.Inventory_10),
    ];

    private ushort? selectedClientId;
    private ItemDetailsPopupHost? popupHost;
    private AdminSlotItemTools? itemTools;
    private LineEdit? moneyEdit;
    private int? lastMoney;
    private bool suppressMoneyCallbacks;
    private readonly Dictionary<BelongingSlot, Control> slotPanels = new();
    private readonly Dictionary<BelongingSlot, TextureRect> slotIcons = new();
    private readonly Dictionary<BelongingSlot, ColorRect> slotUnmetBgs = new();
    private readonly Dictionary<BelongingSlot, int?> lastItemIds = new();
    private readonly Dictionary<BelongingSlot, bool> lastUnmet = new();

    private static readonly Color UnmetSlotBg = new(0.72f, 0.08f, 0.08f, 0.55f);

    public void SetPopupHost(ItemDetailsPopupHost host)
    {
        popupHost = host;
        foreach (var hit in slotPanels.Values)
        {
            if (hit is AdminItemSlot slot)
            {
                slot.PopupHost = host;
            }
        }
    }

    public void SetItemTools(AdminSlotItemTools tools) => itemTools = tools;

    public override void _Ready()
    {
        var maxHeight = MaxWidth * DesignHeight / DesignWidth;
        CustomMinimumSize = new Vector2(MaxWidth, maxHeight);
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Embedded ? SizeFlags.ShrinkCenter : SizeFlags.ShrinkBegin;
        ClipContents = true;

        if (Embedded)
        {
            AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        }
        else
        {
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
        }

        var aspect = new AspectRatioContainer
        {
            Ratio = DesignWidth / DesignHeight,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            StretchMode = AspectRatioContainer.StretchModeEnum.Fit,
            AlignmentHorizontal = AspectRatioContainer.AlignmentMode.Center,
            AlignmentVertical = AspectRatioContainer.AlignmentMode.Center
        };
        AddChild(aspect);

        var frame = new Control { MouseFilter = MouseFilterEnum.Ignore };
        aspect.AddChild(frame);

        var background = new TextureRect
        {
            Texture = AdminUiAtlas.InventoryBackground,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(background);

        foreach (var (rect, slot) in SlotLayout)
        {
            var (hit, icon, unmetBg) = CreateOverlaySlot(rect, slot);
            frame.AddChild(hit);
            slotPanels[slot] = hit;
            slotIcons[slot] = icon;
            slotUnmetBgs[slot] = unmetBg;
        }

        moneyEdit = new LineEdit
        {
            Alignment = HorizontalAlignment.Center,
            ContextMenuEnabled = false,
            SelectAllOnFocus = true,
            CaretBlink = true,
            MouseFilter = MouseFilterEnum.Stop,
            Editable = false
        };
        moneyEdit.AnchorLeft = MoneyFieldLeft / DesignWidth;
        moneyEdit.AnchorTop = MoneyFieldTop / DesignHeight;
        moneyEdit.AnchorRight = MoneyFieldRight / DesignWidth;
        moneyEdit.AnchorBottom = MoneyFieldBottom / DesignHeight;
        moneyEdit.OffsetLeft = 0;
        moneyEdit.OffsetTop = 0;
        moneyEdit.OffsetRight = 0;
        moneyEdit.OffsetBottom = 0;
        moneyEdit.AddThemeColorOverride("font_color", MoneyTextColor);
        moneyEdit.AddThemeColorOverride("font_placeholder_color", MoneyTextColor);
        moneyEdit.AddThemeColorOverride("caret_color", MoneyTextColor);
        moneyEdit.AddThemeFontSizeOverride("font_size", 14);
        var moneyStyle = new StyleBoxEmpty();
        moneyEdit.AddThemeStyleboxOverride("normal", moneyStyle);
        moneyEdit.AddThemeStyleboxOverride("focus", moneyStyle);
        moneyEdit.AddThemeStyleboxOverride("read_only", moneyStyle);
        moneyEdit.TextSubmitted += _ => CommitMoney();
        moneyEdit.FocusExited += CommitMoney;
        frame.AddChild(moneyEdit);

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
            lastMoney = null;
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
        // Invisible hit target — Panel themes can draw unwanted chrome on hover/focus.
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

        var unmetBg = new ColorRect
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
        hit.AddChild(unmetBg);
        var icon = CreateSlotIcon(null);
        hit.AddChild(icon);
        hit.Icon = icon;
        WireSlotInput(hit, slot);
        return (hit, icon, unmetBg);
    }

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
            // Inset 1px so the art sits inside the drawn slot border.
            AnchorsPreset = (int)LayoutPreset.FullRect,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 1,
            OffsetTop = 1,
            OffsetRight = -1,
            OffsetBottom = -1
        };

    public void SetSelectedClient(ushort? clientId)
    {
        selectedClientId = clientId;
        lastItemIds.Clear();
        lastUnmet.Clear();
        lastMoney = null;
        RequestRefresh();
    }

    private void Refresh()
    {
        var character = selectedClientId is null
            ? null
            : ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter;
        RefreshSlotIcons(character);
        RefreshMoney(character);
    }

    private void RefreshMoney(CharacterDbEntry? character)
    {
        if (moneyEdit is null)
        {
            return;
        }

        moneyEdit.Editable = character is not null;
        int? money = character?.Money;
        if (lastMoney == money)
        {
            return;
        }

        lastMoney = money;
        if (moneyEdit.HasFocus())
        {
            return;
        }

        suppressMoneyCallbacks = true;
        moneyEdit.Text = money is null ? string.Empty : FormatMoney(money.Value);
        suppressMoneyCallbacks = false;
    }

    private void CommitMoney()
    {
        if (suppressMoneyCallbacks || moneyEdit is null)
        {
            return;
        }

        if (selectedClientId is null)
        {
            moneyEdit.Text = string.Empty;
            return;
        }

        if (!TryParseMoney(moneyEdit.Text, out var money))
        {
            moneyEdit.Text = lastMoney is null ? string.Empty : FormatMoney(lastMoney.Value);
            return;
        }

        if (lastMoney == money)
        {
            moneyEdit.Text = FormatMoney(money);
            return;
        }

        if (!AdminClientActions.SetMoney(selectedClientId.Value, money))
        {
            moneyEdit.Text = lastMoney is null ? string.Empty : FormatMoney(lastMoney.Value);
            return;
        }

        lastMoney = money;
        moneyEdit.Text = FormatMoney(money);
    }

    private static bool TryParseMoney(string text, out int money)
    {
        var cleaned = text.Trim();
        if (cleaned.EndsWith("t", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^1].Trim();
        }

        cleaned = cleaned.Replace(" ", string.Empty).Replace(",", string.Empty);
        return int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out money)
               && money >= 0;
    }

    private static string FormatMoney(int amount)
    {
        var negative = amount < 0;
        var s = (negative ? -amount : amount).ToString();
        var groups = (s.Length + 2) / 3;
        var result = new char[(negative ? 1 : 0) + s.Length + groups - 1];
        var ri = result.Length - 1;
        var count = 0;
        for (var i = s.Length - 1; i >= 0; i--)
        {
            if (count == 3)
            {
                result[ri--] = ' ';
                count = 0;
            }

            result[ri--] = s[i];
            count++;
        }

        if (negative)
        {
            result[0] = '-';
        }

        return $"{new string(result)} t";
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
            Texture2D? texture = null;
            if (itemId is not null)
            {
                var item = DbConnection.Items.FindById(itemId.Value);
                if (item is not null)
                {
                    texture = AdminUiAtlas.ItemIcon(item.ModelNameInventory);
                    unmet = character is not null && !character.CanUseItem(item);
                }
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
}
