using Godot;
using SphServer.Shared.Db;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Inventory / persona cell. Drag copies the icon; the original stays until a valid drop
///     moves or swaps. Equip rules match the client: slot type plus requirements when wearing.
/// </summary>
public partial class AdminItemSlot : Control
{
    public BelongingSlot Slot { get; set; }
    public Func<ushort?>? GetClientId { get; set; }
    public ItemDetailsPopupHost? PopupHost { get; set; }
    public TextureRect? Icon { get; set; }
    public event Action<BelongingSlot, Vector2>? ContextRequested;

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
                DoubleClick: true,
                ShiftPressed: false
            })
        {
            if (GetClientId?.Invoke() is { } clientId)
            {
                PopupHost?.HideHover();
                AdminClientActions.TryDoubleClickSlot(clientId, Slot);
            }

            AcceptEvent();
            return;
        }

        if (inputEvent is not InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Right
            } button)
        {
            return;
        }

        PopupHost?.HideHover();
        ContextRequested?.Invoke(Slot, button.GlobalPosition);
        AcceptEvent();
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Input.IsKeyPressed(Key.Shift))
        {
            return default;
        }

        if (GetClientId?.Invoke() is not { } clientId
            || ActiveClients.Get(clientId)?.CurrentCharacter is not { } character
            || !character.Items.TryGetValue(Slot, out var itemId)
            || DbConnection.Items.FindById(itemId) is null)
        {
            return default;
        }

        PopupHost?.HideHover();
        var uiScale = GetGlobalTransform().Scale;
        var preview = MakeDragCopy();
        SetDragPreview(preview);
        preview.Scale = uiScale;
        preview.Position = -atPosition * uiScale;

        return new global::Godot.Collections.Dictionary
        {
            { "clientId", (int)clientId },
            { "fromSlot", (int)Slot },
            { "itemId", itemId }
        };
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return TryReadPayload(data, out var clientId, out var from)
               && GetClientId?.Invoke() is { } mine
               && mine == clientId
               && AdminClientActions.CanMoveOrSwapItem(clientId, from, Slot);
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!TryReadPayload(data, out var clientId, out var from)
            || GetClientId?.Invoke() is not { } mine
            || mine != clientId)
        {
            return;
        }

        AdminClientActions.TryMoveOrSwapItem(clientId, from, Slot);
    }

    private static bool TryReadPayload(Variant data, out ushort clientId, out BelongingSlot from)
    {
        clientId = 0;
        from = BelongingSlot.Unknown;
        if (data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        var dict = data.AsGodotDictionary();
        if (!dict.ContainsKey("clientId") || !dict.ContainsKey("fromSlot"))
        {
            return false;
        }

        clientId = (ushort)dict["clientId"].AsInt32();
        from = (BelongingSlot)dict["fromSlot"].AsInt32();
        return true;
    }

    private Control MakeDragCopy()
    {
        var size = Size.X > 1 && Size.Y > 1 ? Size : new Vector2(32, 32);
        if (Icon?.Texture is { } texture)
        {
            return new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = TextureFilterEnum.Nearest,
                MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = size,
                Size = size
            };
        }

        return new ColorRect
        {
            Color = new Color(0.9f, 0.85f, 0.55f, 0.85f),
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = size,
            Size = size
        };
    }
}
