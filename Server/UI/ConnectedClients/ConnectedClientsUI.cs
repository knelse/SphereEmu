using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer.Client;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.ConnectedClients;

public partial class ConnectedClientsUI : Tree
{
    public event Action<Guid>? ClientSelected;

    private static readonly Dictionary<Guid, SphereClient> clients = new ();
    private static TreeItem RootInstance = null!;
    private static Tree TreeInstance = null!;
    private const string DefaultEmptyValue = "<empty>";
    private const int ColumnCount = 3;
    private bool setupSuccessful = true;
    private bool refreshPending;
    private ConnectedClientsPopupUI? popupMenu;
    private Guid? selectedClientId;

    private static readonly string[] ColumnNames = ["ID", "IP address", "Name"];

    public override void _Ready ()
    {
        if (ColumnCount != ColumnNames.Length)
        {
            SphLogger.Error (
                $"ConnectedClientsUI: Column count mismatch. Name count: {ColumnNames.Length}, actual columns: {ColumnCount}");
            setupSuccessful = false;
            return;
        }

        Columns = ColumnCount;
        SelectMode = SelectModeEnum.Row;
        AllowReselect = true;
        popupMenu = FindChild ("ConnectedClientPopup", recursive: true) as ConnectedClientsPopupUI
                    ?? GetNodeOrNull<ConnectedClientsPopupUI> ("ConnectedClientPopup");

        SetColumnTitle (0, ColumnNames[0]);
        SetColumnTitleAlignment (0, HorizontalAlignment.Center);
        SetColumnCustomMinimumWidth (0, 48);
        SetColumnExpand (0, false);
        SetColumnClipContent (0, false);

        SetColumnTitle (1, ColumnNames[1]);
        SetColumnTitleAlignment (1, HorizontalAlignment.Center);
        SetColumnCustomMinimumWidth (1, 120);
        SetColumnExpand (1, false);
        SetColumnClipContent (1, false);

        SetColumnTitle (2, ColumnNames[2]);
        SetColumnTitleAlignment (2, HorizontalAlignment.Center);
        SetColumnExpand (2, true);
        SetColumnExpandRatio (2, 1);
        SetColumnClipContent (2, true);

        RootInstance = CreateItem ();
        TreeInstance = this;
        ItemSelected += OnItemSelected;
        NothingSelected += OnNothingSelected;

        ClientStateEvents.RosterChanged += OnClientStateChanged;
        ClientStateEvents.CharacterChanged += OnCharacterChanged;
        RequestRefresh ();
    }

    public override void _ExitTree ()
    {
        ClientStateEvents.RosterChanged -= OnClientStateChanged;
        ClientStateEvents.CharacterChanged -= OnCharacterChanged;
    }

    private void OnClientStateChanged () => RequestRefresh ();

    private void OnCharacterChanged (Guid _) => RequestRefresh ();

    private void RequestRefresh ()
    {
        if (!setupSuccessful || refreshPending)
        {
            return;
        }

        refreshPending = true;
        CallDeferred (nameof (DeferredRefresh));
    }

    private void DeferredRefresh ()
    {
        refreshPending = false;
        var connectedId = UpdateClientList ();
        if (selectedClientId is not null || connectedId is not Guid id)
        {
            return;
        }

        FindRowByClientId (id)?.Select (0);
    }

    public override void _GuiInput (InputEvent inputEvent)
    {
        // ItemSelected misses a re-click of the same row after a clear; TreeItem.Select re-enters
        // ItemSelected
        if (inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } leftClick)
        {
            var item = GetItemAtPosition (leftClick.Position);
            if (item is not null && item.GetParent () == GetRoot ())
            {
                if (Guid.TryParse (item.GetMetadata (0).AsString (), out var id))
                {
                    NotifyClientSelected (id);
                }
            }
        }

        if (inputEvent is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouseEvent)
        {
            return;
        }

        var rightItem = GetItemAtPosition (mouseEvent.Position);
        if (rightItem is null || popupMenu is null)
        {
            return;
        }

        if (!Guid.TryParse (rightItem.GetMetadata (0).AsString (), out var picked))
        {
            return;
        }

        popupMenu.currentClientId = picked;
        popupMenu.PopupOnParent (new Rect2I (
            new Vector2I ((int) mouseEvent.GlobalPosition.X, (int) mouseEvent.GlobalPosition.Y), Vector2I.Zero));
    }

    private void OnItemSelected ()
    {
        var item = GetSelected ();
        if (item is null)
        {
            return;
        }

        if (!Guid.TryParse (item.GetMetadata (0).AsString (), out var id))
        {
            return;
        }

        NotifyClientSelected (id);
    }

    private void NotifyClientSelected (Guid id)
    {
        selectedClientId = id;
        ClientSelected?.Invoke (id);
    }

    private void OnNothingSelected ()
    {
        selectedClientId = null;
        ClientSelected?.Invoke (Guid.Empty);
    }

    private static Guid? UpdateClientList ()
    {
        var actualClients = ActiveClients.GetAll ();

        var disconnectedClients = clients.Where (x => !actualClients.ContainsKey (x.Key)).ToList ();
        foreach (var disconnectedClientData in disconnectedClients)
        {
            clients.Remove (disconnectedClientData.Key);
            DeleteClientRow (disconnectedClientData.Key);
        }

        Guid? connectedId = null;
        foreach (var clientData in actualClients)
        {
            if (clients.ContainsKey (clientData.Key))
            {
                UpdateClientRow (clientData.Key, clientData.Value);
                continue;
            }

            clients.Add (clientData.Key, clientData.Value);
            AddClientRow (clientData.Key, clientData.Value);
            if (connectedId is null && !clientData.Value.IsAdminDebugDummy)
            {
                connectedId = clientData.Key;
            }
        }

        return connectedId;
    }

    private static void AddClientRow (Guid id, SphereClient client)
    {
        var clientItem = TreeInstance.CreateItem (RootInstance);
        for (var i = 0; i < ColumnCount; i++)
        {
            UpdateColumnStyle (clientItem, i);
        }

        clientItem.SetMetadata (0, id.ToString ());
        clientItem.SetText (0, FormatClientId (id));
        clientItem.SetText (1, client.GetIpAddressWithoutPort ());
        SetDisplayDataForClient (clientItem, client);
    }

    private static void SetDisplayDataForClient (TreeItem clientItem, SphereClient client)
    {
        var character = client.CurrentCharacter;
        clientItem.SetText (2, character?.Name ?? DefaultEmptyValue);
    }

    private static void UpdateColumnStyle (TreeItem item, int column)
    {
        item.SetCustomFontSize (column, 12);
        item.SetTextAlignment (column, HorizontalAlignment.Center);
        item.SetSelectable (column, true);
    }

    private static string FormatClientId (Guid id) => id.ToString ();

    private static void DeleteClientRow (Guid id)
    {
        var rowToRemove = FindRowByClientId (id);
        if (rowToRemove is null)
        {
            SphLogger.Warning ($"ConnectedClientsUI: unable to delete row for client ID: {FormatClientId (id)}");
            return;
        }

        RootInstance.RemoveChild (rowToRemove);
    }

    private static void UpdateClientRow (Guid id, SphereClient client)
    {
        var rowToUpdate = FindRowByClientId (id);
        if (rowToUpdate is null)
        {
            return;
        }

        rowToUpdate.SetText (0, FormatClientId (id));
        rowToUpdate.SetText (1, client.GetIpAddressWithoutPort ());
        SetDisplayDataForClient (rowToUpdate, client);
    }

    private static TreeItem? FindRowByClientId (Guid id)
    {
        var text = id.ToString ();
        return RootInstance.GetChildren ()
            .Where (x => x.GetMetadata (0).AsString () == text).FirstOrDefault ();
    }
}
