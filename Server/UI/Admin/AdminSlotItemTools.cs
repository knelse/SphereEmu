using Godot;
using SphServer.Server.UI.Localization;
using SphServer.Shared.Db;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Right-click Clear / Change for inventory and persona slots.
/// </summary>
public partial class AdminSlotItemTools : Node
{
    private const int ClearId = 0;
    private const int ChangeId = 1;

    private PopupMenu? menu;
    private ConfirmationDialog? deleteDialog;
    private AdminItemSelectWindow? selectWindow;
    private Locale locale = Locale.Russian;
    private ushort pendingClientId;
    private BelongingSlot pendingSlot;

    public void SetLocale(Locale newLocale)
    {
        locale = newLocale;
        selectWindow?.SetLocale(newLocale);
    }

    public override void _Ready()
    {
        menu = new PopupMenu { Name = "SlotContextMenu" };
        menu.AddItem("Clear", ClearId);
        menu.AddItem("Change", ChangeId);
        menu.IdPressed += OnMenuIdPressed;
        AddChild(menu);

        deleteDialog = new ConfirmationDialog
        {
            Name = "DeleteSlotItemDialog",
            Title = "Clear item",
            MinSize = new Vector2I(360, 120),
            Exclusive = true,
            Unresizable = true,
            OkButtonText = "Confirm",
            CancelButtonText = "Cancel"
        };
        deleteDialog.Confirmed += OnDeleteConfirmed;
        AddChild(deleteDialog);
        deleteDialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;

        selectWindow = new AdminItemSelectWindow { Name = "ItemSelectWindow" };
        AddChild(selectWindow);
        selectWindow.SetLocale(locale);
    }

    public void OpenMenu(ushort clientId, BelongingSlot slot, Vector2 globalPos)
    {
        if (menu is null)
        {
            return;
        }

        pendingClientId = clientId;
        pendingSlot = slot;
        var occupied = ActiveClients.Get(clientId)?.CurrentCharacter?.Items.ContainsKey(slot) == true;
        var clearIndex = menu.GetItemIndex(ClearId);
        if (clearIndex >= 0)
        {
            menu.SetItemDisabled(clearIndex, !occupied);
        }

        menu.PopupOnParent(new Rect2I(new Vector2I((int)globalPos.X, (int)globalPos.Y), Vector2I.Zero));
    }

    private void OnMenuIdPressed(long id)
    {
        if (id == ClearId)
        {
            OpenDeleteConfirm();
            return;
        }

        if (id == ChangeId)
        {
            selectWindow?.SetLocale(locale);
            selectWindow?.OpenFor(pendingClientId, pendingSlot);
        }
    }

    private void OpenDeleteConfirm()
    {
        if (deleteDialog is null)
        {
            return;
        }

        var character = ActiveClients.Get(pendingClientId)?.CurrentCharacter;
        if (character is null || !character.Items.TryGetValue(pendingSlot, out var itemId))
        {
            return;
        }

        var item = DbConnection.Items.FindById(itemId);
        var name = item is null ? "?" : ItemLocaleText.DisplayName(item, locale);
        deleteDialog.DialogText = $"Delete {name}?";
        deleteDialog.PopupCentered();
    }

    private void OnDeleteConfirmed()
    {
        AdminClientActions.ClearSlotItem(pendingClientId, pendingSlot);
    }
}
