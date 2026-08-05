using System.Collections.Generic;
using Godot;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Persona sketch: custom gender background + slot overlay + transparent item panels,
///     plus a mutator column (<see cref="BelongingSlot.Mutator_1"/>…<c>10</c>) on the right.
///     Art is 256×370 under <c>ui_custom/</c>. Slot panels are 33×32 centered on overlay icons.
/// </summary>
public partial class PersonaPanel : PanelContainer
{
    private const float DesignWidth = 256f;
    private const float DesignHeight = 370f;
    private const float MaxWidth = 500f;
    private const float SlotWidth = 33f;
    private const float SlotHeight = 32f;
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

    /// <summary>Overlay-space center → <see cref="BelongingSlot"/>.</summary>
    private static readonly (Vector2 Center, BelongingSlot Slot)[] SlotLayout =
    [
        // Far left — special
        (new(31, 56), BelongingSlot.Special_1),
        (new(31, 91), BelongingSlot.Special_2),
        (new(31, 126), BelongingSlot.Special_3),
        (new(31, 161), BelongingSlot.Special_4),
        (new(31, 196), BelongingSlot.Special_5),
        (new(31, 231), BelongingSlot.Special_6),
        (new(31, 266), BelongingSlot.Special_7),
        (new(31, 301), BelongingSlot.Special_8),
        (new(31, 336), BelongingSlot.Special_9),

        // Inner left
        (new(80, 56), BelongingSlot.Helmet),
        (new(80, 91), BelongingSlot.Amulet),
        (new(79, 161), BelongingSlot.Shield),
        (new(79, 196), BelongingSlot.BraceletLeft),
        (new(79, 231), BelongingSlot.Ring_1),
        (new(79, 266), BelongingSlot.Ring_2),

        // Center (armor)
        (new(123, 129), BelongingSlot.Chestplate),
        (new(123, 164), BelongingSlot.Belt),
        (new(123, 198), BelongingSlot.Pants),
        (new(123, 300), BelongingSlot.Boots),

        // Inner right
        (new(169, 89), BelongingSlot.Guild),
        (new(168, 164), BelongingSlot.Gloves),
        (new(168, 198), BelongingSlot.BraceletRight),
        (new(168, 232), BelongingSlot.Ring_3),
        (new(168, 266), BelongingSlot.Ring_4),

        // Far right
        (new(215, 74), BelongingSlot.MainHand),
        (new(215, 108), BelongingSlot.Ammo),
        (new(215, 164), BelongingSlot.MapBook),
        (new(215, 198), BelongingSlot.RecipeBook),
        (new(215, 232), BelongingSlot.MantraBook),
        (new(215, 266), BelongingSlot.Inkpot),
        (new(215, 300), BelongingSlot.TokenIsland),
        (new(215, 334), BelongingSlot.SpeedhackMantra),
    ];

    private ushort? selectedClientId;
    private TextureRect? background;
    private readonly Dictionary<BelongingSlot, Panel> slotPanels = new();
    private readonly Dictionary<BelongingSlot, TextureRect> mutatorIcons = new();

    public override void _Ready()
    {
        var maxHeight = MaxWidth * DesignHeight / DesignWidth;
        CustomMinimumSize = new Vector2(MaxWidth + MutatorSize + 8f, maxHeight);
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

        foreach (var (center, slot) in SlotLayout)
        {
            var panel = CreateSlotPanel(center, slot);
            frame.AddChild(panel);
            slotPanels[slot] = panel;
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
            var (panel, icon) = CreateMutatorSlot(slot);
            mutatorColumn.AddChild(panel);
            slotPanels[slot] = panel;
            mutatorIcons[slot] = icon;
        }
    }

    private static Panel CreateSlotPanel(Vector2 center, BelongingSlot slot)
    {
        var halfW = SlotWidth * 0.5f;
        var halfH = SlotHeight * 0.5f;
        var panel = new Panel
        {
            Name = $"Slot_{slot}",
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.SetMeta("BelongingSlot", (int)slot);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        panel.AnchorLeft = (center.X - halfW) / DesignWidth;
        panel.AnchorTop = (center.Y - halfH) / DesignHeight;
        panel.AnchorRight = (center.X + halfW) / DesignWidth;
        panel.AnchorBottom = (center.Y + halfH) / DesignHeight;
        panel.OffsetLeft = 0;
        panel.OffsetTop = 0;
        panel.OffsetRight = 0;
        panel.OffsetBottom = 0;
        return panel;
    }

    private static (Panel Panel, TextureRect Icon) CreateMutatorSlot(BelongingSlot slot)
    {
        var panel = new Panel
        {
            Name = $"Slot_{slot}",
            CustomMinimumSize = new Vector2(MutatorSize, MutatorSize),
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.SetMeta("BelongingSlot", (int)slot);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

        var icon = new TextureRect
        {
            Texture = AdminUiAtlas.MutatorPlaceholder,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore
        };
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.AddChild(icon);
        return (panel, icon);
    }

    public Panel? GetSlotPanel(BelongingSlot slot) =>
        slotPanels.TryGetValue(slot, out var panel) ? panel : null;

    public void SetSelectedClient(ushort? clientId)
    {
        selectedClientId = clientId;
        Refresh();
    }

    public override void _Process(double delta) => Refresh();

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

        var placeholder = AdminUiAtlas.MutatorPlaceholder;
        foreach (var (slot, icon) in mutatorIcons)
        {
            var empty = character is null || character.IsItemSlotEmpty(slot);
            icon.Texture = empty ? placeholder : null;
        }
    }
}
