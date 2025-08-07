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
    private static readonly Dictionary<ushort, SphereClient> clients = new ();
    private static TreeItem RootInstance = null!;
    private static Tree TreeInstance = null!;
    private const string DEFAULT_EMPTY_VALUE = "<empty>";
    private const int COLUMN_COUNT = 9;
    private bool SetupSuccessful = true;
    private ConnectedClientsPopupUI popupMenu = null;

    private static readonly string[] ColumnNames =
    [
        "ID", "IP address", "Name", "X", "Y", "Z", "Angle", "Level", "HP"
    ];

    public override void _Ready ()
    {
        if (COLUMN_COUNT != ColumnNames.Length)
        {
            SphLogger.Error(
                $"ConnectedClientsUI: Column count mismatch. Name count: {ColumnNames.Length}, actual columns: {COLUMN_COUNT}");
            SetupSuccessful = false;
            return;
        }

        popupMenu = FindChild("ConnectedClientPopup") as ConnectedClientsPopupUI;

        for (var i = 0; i < COLUMN_COUNT; i++)
        {
            SetColumnTitle(i, ColumnNames[i]);
        }

        RootInstance = CreateItem();
        TreeInstance = this;
    }

    public override async void _Process (double delta)
    {
        if (!SetupSuccessful)
        {
            return;
        }

        await UpdateClientList();
    }

    public override void _GuiInput (InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouseEvent)
        {
            return;
        }

        var item = TreeInstance.GetItemAtPosition(mouseEvent.Position);
        if (item is null)
        {
            return;
        }

        popupMenu.currentClientId = item.GetMetadata(0).AsUInt16();
        popupMenu.PopupOnParent(new Rect2I(
            new Vector2I((int) mouseEvent.GlobalPosition.X, (int) mouseEvent.GlobalPosition.Y), Vector2I.Zero));
    }

    private static async Task UpdateClientList ()
    {
        var actualClients = ActiveClients.GetAll();

        var disconnectedClients = clients.Where(x => !actualClients.ContainsKey(x.Key));
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

    private static async Task AddClientRow (ushort id, SphereClient client)
    {
        var clientItem = TreeInstance.CreateItem(RootInstance);
        for (var i = 0; i < COLUMN_COUNT; i++)
        {
            UpdateColumnStyle(clientItem, i);
        }

        // just to be safe and not care about column ordering
        clientItem.SetMetadata(0, id);

        clientItem.SetText(0, $"{id:X4}");
        clientItem.SetText(1, client.GetIpAddressAndPort());
        SetDisplayDataForClient(clientItem, client);
    }

    private static void SetDisplayDataForClient (TreeItem clientItem, SphereClient client)
    {
        var displayData = GetDisplayDataForClient(client);
        for (var i = 2; i < COLUMN_COUNT; i++)
        {
            clientItem.SetText(i, displayData[ColumnNames[i]]);
        }
    }

    private static void UpdateColumnStyle (TreeItem item, int id)
    {
        item.SetCustomFontSize(id, 12);
        item.SetTextAlignment(id, HorizontalAlignment.Center);
        item.SetSelectable(id, false);
    }

    private static Dictionary<string, string> GetDisplayDataForClient (SphereClient client)
    {
        var character = client.CurrentCharacter;

        if (character is null)
        {
            return new Dictionary<string, string>
            {
                [ColumnNames[2]] = DEFAULT_EMPTY_VALUE,
                [ColumnNames[3]] = DEFAULT_EMPTY_VALUE,
                [ColumnNames[4]] = DEFAULT_EMPTY_VALUE,
                [ColumnNames[5]] = DEFAULT_EMPTY_VALUE,
                [ColumnNames[6]] = DEFAULT_EMPTY_VALUE,
                [ColumnNames[7]] = DEFAULT_EMPTY_VALUE,
                [ColumnNames[8]] = DEFAULT_EMPTY_VALUE
            };
        }

        var titleLevel = character.TitleMinusOne % 60 + 1;
        var degreeLevel = character.DegreeMinusOne % 60 + 1;

        var titleTier = titleLevel < 60 ? string.Empty : titleLevel < 120 ? "+" : titleLevel < 180 ? "++" : "+++";
        var degreeTier = degreeLevel < 60 ? string.Empty : degreeLevel < 120 ? "+" : degreeLevel < 180 ? "++" : "+++";

        return new Dictionary<string, string>
        {
            [ColumnNames[2]] = character.Name,
            [ColumnNames[3]] = $"{character.X:F1}",
            [ColumnNames[4]] = $"{character.Y:F1}",
            [ColumnNames[5]] = $"{character.Z:F1}",
            [ColumnNames[6]] = $"{character.Angle:F1}",
            [ColumnNames[7]] = $"{titleLevel}{titleTier}/{degreeLevel}{degreeTier}",
            [ColumnNames[8]] = $"{character.CurrentHP}/{character.MaxHP}"
        };
    }

    private static async Task DeleteClientRow (ushort id)
    {
        var rowToRemove = FindRowByClientId(id);

        if (rowToRemove is null)
        {
            SphLogger.Warning($"ConnectedClientsUI: unable to delete row for client ID: {id:X4}");
            return;
        }

        RootInstance.RemoveChild(rowToRemove);
    }

    private static async Task UpdateClientRow (ushort id, SphereClient client)
    {
        var rowToUpdate = FindRowByClientId(id);

        if (rowToUpdate is null)
        {
            return;
        }

        SetDisplayDataForClient(rowToUpdate, client);
    }

    private static TreeItem? FindRowByClientId (ushort id)
    {
        return RootInstance.GetChildren()
            .Where(x => x.GetMetadata(0).AsUInt16() == id).FirstOrDefault();
    }
}