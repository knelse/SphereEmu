using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SphServer.Client;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.ConnectedClients;

public partial class ConnectedClientsUI : Tree
{
    [Signal]
    public delegate void ClientSelectedEventHandler(ushort clientId);

    // Godot signals can't be nullable ushort easily — use 0 for clear; AdminUiRoot treats missing character separately.
    // Prefer a companion clear via metadata when selection lost.

    private static readonly Dictionary<ushort, SphereClient> clients = new();
    private static TreeItem RootInstance = null!;
    private static Tree TreeInstance = null!;
    private const string DefaultEmptyValue = "<empty>";
    private const int ColumnCount = 3;
    private bool setupSuccessful = true;
    private ConnectedClientsPopupUI? popupMenu;
    private ushort? selectedClientId;

    private static readonly string[] ColumnNames = ["ID", "IP address", "Name"];

    public override void _Ready()
    {
        if (ColumnCount != ColumnNames.Length)
        {
            SphLogger.Error(
                $"ConnectedClientsUI: Column count mismatch. Name count: {ColumnNames.Length}, actual columns: {ColumnCount}");
            setupSuccessful = false;
            return;
        }

        Columns = ColumnCount;
        SelectMode = SelectModeEnum.Row;
        AllowReselect = true;
        popupMenu = FindChild("ConnectedClientPopup", recursive: true) as ConnectedClientsPopupUI
                    ?? GetNodeOrNull<ConnectedClientsPopupUI>("ConnectedClientPopup");

        // Fixed widths for ID (ushort hex) and IP so Name stays under its title
        SetColumnTitle(0, ColumnNames[0]);
        SetColumnTitleAlignment(0, HorizontalAlignment.Center);
        SetColumnCustomMinimumWidth(0, 48);
        SetColumnExpand(0, false);
        SetColumnClipContent(0, false);

        SetColumnTitle(1, ColumnNames[1]);
        SetColumnTitleAlignment(1, HorizontalAlignment.Center);
        SetColumnCustomMinimumWidth(1, 120);
        SetColumnExpand(1, false);
        SetColumnClipContent(1, false);

        SetColumnTitle(2, ColumnNames[2]);
        SetColumnTitleAlignment(2, HorizontalAlignment.Center);
        SetColumnExpand(2, true);
        SetColumnExpandRatio(2, 1);
        SetColumnClipContent(2, true);

        RootInstance = CreateItem();
        TreeInstance = this;
        ItemSelected += OnItemSelected;
        NothingSelected += OnNothingSelected;
    }

    public override async void _Process(double delta)
    {
        if (!setupSuccessful)
        {
            return;
        }

        await UpdateClientList();
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        // Left-click always notifies even when re-clicking the same row after a clear
        // (Tree ItemSelected can miss that). Do not call TreeItem.Select here — it re-enters ItemSelected.
        if (inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } leftClick)
        {
            var item = GetItemAtPosition(leftClick.Position);
            if (item is not null && item.GetParent() == GetRoot())
            {
                NotifyClientSelected(item.GetMetadata(0).AsUInt16());
            }
        }

        if (inputEvent is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouseEvent)
        {
            return;
        }

        var rightItem = GetItemAtPosition(mouseEvent.Position);
        if (rightItem is null || popupMenu is null)
        {
            return;
        }

        popupMenu.currentClientId = rightItem.GetMetadata(0).AsUInt16();
        popupMenu.PopupOnParent(new Rect2I(
            new Vector2I((int)mouseEvent.GlobalPosition.X, (int)mouseEvent.GlobalPosition.Y), Vector2I.Zero));
    }

    private void OnItemSelected()
    {
        var item = GetSelected();
        if (item is null)
        {
            return;
        }

        NotifyClientSelected(item.GetMetadata(0).AsUInt16());
    }

    private void NotifyClientSelected(ushort id)
    {
        selectedClientId = id;
        EmitSignal(SignalName.ClientSelected, id);
    }

    private void OnNothingSelected()
    {
        selectedClientId = null;
        EmitSignal(SignalName.ClientSelected, (ushort)0);
    }

    private static async Task UpdateClientList()
    {
        var actualClients = ActiveClients.GetAll();

        var disconnectedClients = clients.Where(x => !actualClients.ContainsKey(x.Key)).ToList();
        foreach (var disconnectedClientData in disconnectedClients)
        {
            clients.Remove(disconnectedClientData.Key);
            await DeleteClientRow(disconnectedClientData.Key);
        }

        foreach (var clientData in actualClients)
        {
            if (clients.ContainsKey(clientData.Key))
            {
                await UpdateClientRow(clientData.Key, clientData.Value);
                continue;
            }

            clients.Add(clientData.Key, clientData.Value);
            await AddClientRow(clientData.Key, clientData.Value);
        }
    }

    private static async Task AddClientRow(ushort id, SphereClient client)
    {
        var clientItem = TreeInstance.CreateItem(RootInstance);
        for (var i = 0; i < ColumnCount; i++)
        {
            UpdateColumnStyle(clientItem, i);
        }

        clientItem.SetMetadata(0, id);
        clientItem.SetText(0, FormatClientId(id));
        clientItem.SetText(1, client.GetIpAddressWithoutPort());
        SetDisplayDataForClient(clientItem, client);
    }

    private static void SetDisplayDataForClient(TreeItem clientItem, SphereClient client)
    {
        var character = client.CurrentCharacter;
        clientItem.SetText(2, character?.Name ?? DefaultEmptyValue);
    }

    private static void UpdateColumnStyle(TreeItem item, int column)
    {
        item.SetCustomFontSize(column, 12);
        item.SetTextAlignment(column, HorizontalAlignment.Center);
        item.SetSelectable(column, true);
    }

    private static string FormatClientId(ushort id) => id.ToString("X4");

    private static async Task DeleteClientRow(ushort id)
    {
        var rowToRemove = FindRowByClientId(id);
        if (rowToRemove is null)
        {
            SphLogger.Warning($"ConnectedClientsUI: unable to delete row for client ID: {FormatClientId(id)}");
            return;
        }

        RootInstance.RemoveChild(rowToRemove);
    }

    private static async Task UpdateClientRow(ushort id, SphereClient client)
    {
        var rowToUpdate = FindRowByClientId(id);
        if (rowToUpdate is null)
        {
            return;
        }

        rowToUpdate.SetText(0, FormatClientId(id));
        rowToUpdate.SetText(1, client.GetIpAddressWithoutPort());
        SetDisplayDataForClient(rowToUpdate, client);
    }

    private static TreeItem? FindRowByClientId(ushort id)
    {
        return RootInstance.GetChildren()
            .Where(x => x.GetMetadata(0).AsUInt16() == id).FirstOrDefault();
    }
}
