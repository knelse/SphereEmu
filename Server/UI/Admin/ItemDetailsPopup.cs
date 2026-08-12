using Godot;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Ornate popup chrome around <see cref="AdminUiItemDetails"/> content.
/// </summary>
public partial class ItemDetailsPopup : Control
{
    public const float FrameWidth = AdminUiItemDetails.PreferredFrameWidth;
    public const float TopHeight = 30f;
    public const float MidHeight = 17f;
    public const float BottomHeight = 5f;

    private const float ContentMarginLeft = 10f;
    private const float ContentMarginRight = 10f;
    private const float ContentMarginTop = 4f;
    private const float ContentMarginBottom = 4f;

    // Solid underlay so popup_mid keeps its tint but blocks the scene behind it.
    private static readonly Color MidUnderlay = new(0.06f, 0.07f, 0.09f, 0.92f);

    private Control? dragHandle;
    private TextureButton? closeButton;
    private VBoxContainer? contentBox;
    private bool dragging;
    private Vector2 dragOffset;
    private int midTileCount = 4;
    private bool shellBuilt;

    public bool IsPinned { get; private set; }
    public int? ItemId { get; private set; }
    public BelongingSlot? Slot { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        if (!shellBuilt)
        {
            RebuildShell(midTileCount);
        }

        SetPinned(IsPinned);
    }

    public void Bind(int? itemId, BelongingSlot? slot)
    {
        ItemId = itemId;
        Slot = slot;
    }

    public void SetPinned(bool pinned)
    {
        IsPinned = pinned;
        if (closeButton is not null)
        {
            closeButton.Visible = pinned;
        }

        var filter = pinned ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        MouseFilter = filter;
        if (dragHandle is not null)
        {
            dragHandle.MouseFilter = filter;
        }
    }

    public void Populate(ItemDbEntry item, CharacterDbEntry? character, Locale locale)
    {
        ItemId = item.Id;
        midTileCount = EstimateMidTiles(item, locale);
        var wasPinned = IsPinned;

        if (IsInsideTree())
        {
            RebuildShell(midTileCount);
            SetPinned(wasPinned);
            FillContent(item, character, locale);
        }
        else
        {
            // _Ready will build shell; stash fill for after.
            pendingItem = item;
            pendingCharacter = character;
            pendingLocale = locale;
            CallDeferred(nameof(ApplyPendingPopulate));
        }
    }

    private ItemDbEntry? pendingItem;
    private CharacterDbEntry? pendingCharacter;
    private Locale pendingLocale;

    private void ApplyPendingPopulate()
    {
        if (pendingItem is null)
        {
            return;
        }

        var item = pendingItem;
        pendingItem = null;
        RebuildShell(midTileCount);
        SetPinned(IsPinned);
        FillContent(item, pendingCharacter, pendingLocale);
    }

    private static int EstimateMidTiles(ItemDbEntry item, Locale locale)
    {
        var innerW = FrameWidth - ContentMarginLeft - ContentMarginRight;
        var contentH = AdminUiItemDetails.EstimateContentHeight(
            item, locale, innerW, ContentMarginTop, ContentMarginBottom);
        return Mathf.Max(2, Mathf.CeilToInt(contentH / MidHeight));
    }

    private void FillContent(ItemDbEntry item, CharacterDbEntry? character, Locale locale)
    {
        if (contentBox is null)
        {
            return;
        }

        AdminUiItemDetails.Fill(contentBox, item, character, locale);
    }

    private void RebuildShell(int midTiles)
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        dragHandle = null;
        closeButton = null;
        contentBox = null;
        midTileCount = midTiles;
        shellBuilt = true;

        CustomMinimumSize = new Vector2(FrameWidth, TopHeight + midTiles * MidHeight + BottomHeight);
        Size = CustomMinimumSize;

        var midBodyH = midTiles * MidHeight;
        AddChild(new ColorRect
        {
            Color = MidUnderlay,
            Position = new Vector2(0, TopHeight),
            Size = new Vector2(FrameWidth, midBodyH),
            MouseFilter = MouseFilterEnum.Ignore
        });

        var chromeStack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        chromeStack.AddThemeConstantOverride("separation", 0);
        chromeStack.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(chromeStack);

        dragHandle = new Control
        {
            CustomMinimumSize = new Vector2(FrameWidth, TopHeight),
            MouseFilter = MouseFilterEnum.Stop
        };
        chromeStack.AddChild(dragHandle);

        var top = new TextureRect
        {
            Texture = AdminUiAtlas.PopupTop,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            CustomMinimumSize = new Vector2(FrameWidth, TopHeight),
            MouseFilter = MouseFilterEnum.Ignore
        };
        top.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dragHandle.AddChild(top);

        const float closeW = 20f;
        const float closeH = 20f;
        // Sit just above the bottom of popup_top (1px inset).
        var closeTop = TopHeight - closeH - 1f;
        closeButton = new TextureButton
        {
            TextureNormal = AdminUiAtlas.CloseButton,
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            CustomMinimumSize = new Vector2(closeW, closeH),
            Visible = false
        };
        closeButton.SetAnchorsPreset(LayoutPreset.TopRight);
        closeButton.AnchorLeft = 1;
        closeButton.AnchorRight = 1;
        closeButton.GrowHorizontal = GrowDirection.Begin;
        closeButton.OffsetLeft = -closeW - 4;
        closeButton.OffsetTop = closeTop;
        closeButton.OffsetRight = -4;
        closeButton.OffsetBottom = closeTop + closeH;
        closeButton.Pressed += OnClosePressed;
        dragHandle.AddChild(closeButton);
        dragHandle.GuiInput += OnDragHandleGuiInput;

        for (var i = 0; i < midTiles; i++)
        {
            chromeStack.AddChild(new TextureRect
            {
                Texture = AdminUiAtlas.PopupMid,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = TextureFilterEnum.Nearest,
                CustomMinimumSize = new Vector2(FrameWidth, MidHeight),
                MouseFilter = MouseFilterEnum.Ignore
            });
        }

        chromeStack.AddChild(new TextureRect
        {
            Texture = AdminUiAtlas.PopupBottom,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            CustomMinimumSize = new Vector2(FrameWidth, BottomHeight),
            MouseFilter = MouseFilterEnum.Ignore
        });

        var contentMargin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        contentMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        contentMargin.OffsetTop = TopHeight + ContentMarginTop;
        contentMargin.OffsetBottom = -BottomHeight - ContentMarginBottom;
        contentMargin.OffsetLeft = ContentMarginLeft;
        contentMargin.OffsetRight = -ContentMarginRight;
        AddChild(contentMargin);

        contentBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        contentBox.AddThemeConstantOverride("separation", 2);
        contentMargin.AddChild(contentBox);
    }

    private void OnClosePressed()
    {
        if (IsPinned)
        {
            QueueFree();
        }
    }

    private void OnDragHandleGuiInput(InputEvent inputEvent)
    {
        if (!IsPinned)
        {
            return;
        }

        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                if (closeButton is not null && closeButton.GetGlobalRect().HasPoint(button.GlobalPosition))
                {
                    return;
                }

                BringToFront();
                dragging = true;
                dragOffset = GlobalPosition - button.GlobalPosition;
                AcceptEvent();
            }
            else
            {
                dragging = false;
            }
        }
        else if (dragging && inputEvent is InputEventMouseMotion motion)
        {
            GlobalPosition = motion.GlobalPosition + dragOffset;
            AcceptEvent();
        }
    }

    public void BringToFront()
    {
        var parent = GetParent();
        parent?.MoveChild(this, parent.GetChildCount() - 1);
    }
}
