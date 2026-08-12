using Godot;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Overlay for item detail popups. At most one hover popup; any number of pinned ones.
/// </summary>
public partial class ItemDetailsPopupHost : Control
{
    private ItemDetailsPopup? hoverPopup;
    private BelongingSlot? hoverSlot;
    private Locale locale = Locale.Russian;
    private static readonly Vector2 HoverOffset = new(36, 0);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetLocale(Locale newLocale) => locale = newLocale;

    public void ShowHover(Vector2 globalAnchor, int itemId, BelongingSlot slot, CharacterDbEntry? character)
    {
        var item = DbConnection.Items.FindById(itemId);
        if (item is null)
        {
            HideHover();
            return;
        }

        if (hoverPopup is not null
            && GodotObject.IsInstanceValid(hoverPopup)
            && hoverPopup.Visible
            && hoverSlot == slot
            && hoverPopup.ItemId == itemId)
        {
            return;
        }

        if (hoverPopup is null || !GodotObject.IsInstanceValid(hoverPopup))
        {
            hoverPopup = new ItemDetailsPopup();
            AddChild(hoverPopup);
        }

        hoverSlot = slot;
        hoverPopup.Bind(itemId, slot);
        hoverPopup.SetPinned(false);
        hoverPopup.Visible = true;
        hoverPopup.Populate(item, character, locale);
        PositionPopup(hoverPopup, globalAnchor);
        MoveChild(hoverPopup, 0);
    }

    public void HideHover(BelongingSlot? slot = null)
    {
        if (slot is not null && hoverSlot is not null && slot != hoverSlot)
        {
            return;
        }

        hoverSlot = null;
        if (hoverPopup is null || !GodotObject.IsInstanceValid(hoverPopup))
        {
            hoverPopup = null;
            return;
        }

        hoverPopup.Visible = false;
    }

    public void Pin(Vector2 globalAnchor, int itemId, BelongingSlot slot, CharacterDbEntry? character)
    {
        var item = DbConnection.Items.FindById(itemId);
        if (item is null)
        {
            return;
        }

        HideHover();

        var pinned = new ItemDetailsPopup();
        AddChild(pinned);
        pinned.Bind(itemId, slot);
        pinned.SetPinned(true);
        pinned.Visible = true;
        pinned.Populate(item, character, locale);
        PositionPopup(pinned, globalAnchor);
        pinned.BringToFront();
    }

    private void PositionPopup(ItemDetailsPopup popup, Vector2 globalAnchor)
    {
        var desired = globalAnchor + HoverOffset;
        var host = GetGlobalRect();
        var size = popup.Size;
        if (size == Vector2.Zero)
        {
            size = popup.CustomMinimumSize;
        }

        desired.X = Mathf.Clamp(desired.X, host.Position.X, host.Position.X + Mathf.Max(0, host.Size.X - size.X));
        desired.Y = Mathf.Clamp(desired.Y, host.Position.Y, host.Position.Y + Mathf.Max(0, host.Size.Y - size.Y));
        popup.GlobalPosition = desired;
    }
}
