using System;
using System.Collections.Generic;
using Godot;
using SphServer.Helpers;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.ConnectedClients;

public partial class ConnectedClientsPopupUI : PopupMenu
{
    public ushort currentClientId;
    public override void _Ready ()
    {
        CreateTeleportMenuHierarchy();
    }

    private void CreateTeleportMenuHierarchy ()
    {
        var teleportMenu = new PopupMenu();
        teleportMenu.Name = "Teleport";
        
        foreach (var continent in Enum.GetValues<Continents>())
        {
            var submenu = CreateSubmenuForContinent(continent);
            submenu.Name = continent.ToString();
            teleportMenu.AddSubmenuNodeItem(continent.ToString(), submenu);
        }
        
        AddSubmenuNodeItem("Teleport", teleportMenu);
    }

    private PopupMenu CreateSubmenuForContinent (Continents continent)
    {
        var continentMenu = new PopupMenu();
        continentMenu.Name = continent.ToString();
        var respawnPoints = new PopupMenu();
        respawnPoints.Name = "RespawnPoints";
        
        var respawnsForContinent = SavedCoords.RespawnPoints.GetValueOrDefault(continent, []);

        foreach (var respawnsForCity in respawnsForContinent)
        {
            var cityMenu = new PopupMenu();
            cityMenu.Name = respawnsForCity.Key.ToString();
            foreach (var respawnPoint in respawnsForCity.Value)
            {
                var label = $"{respawnPoint.Key.ToString()} [{respawnPoint.Value}]";
                cityMenu.AddItem(label);
            }

            cityMenu.IndexPressed += GenerateOnIndexPressedForMenu(cityMenu);
            
            respawnPoints.AddSubmenuNodeItem(respawnsForCity.Key.ToString(), cityMenu);
        }
        
        continentMenu.AddSubmenuNodeItem("Respawn points", respawnPoints);
        
        var poiForContinent = SavedCoords.TeleportPoints.GetValueOrDefault(continent, []);

        foreach (var poiForCity in poiForContinent)
        {
            var poiMenu = new PopupMenu();
            poiMenu.Name = poiForCity.Key.ToString();
            foreach (var poi in poiForCity.Value)
            {
                poiMenu.AddItem($"{poi.Key} [{poi.Value}]");
            }

            poiMenu.IndexPressed += GenerateOnIndexPressedForMenu(poiMenu);
            continentMenu.AddSubmenuNodeItem(poiForCity.Key.ToString(), poiMenu);
        }

        return continentMenu;
    }
            
    // TODO: this is awful and relies on item text, but good enough for now
    private IndexPressedEventHandler GenerateOnIndexPressedForMenu (PopupMenu popupMenu)
    {
        return index =>
        {
            var itemText = popupMenu.GetItemText((int) index);

            if (itemText is null)
            {
                return;
            }

            var coordsString =
                itemText.Split('[', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[1];

            var coordsSplit = coordsString[..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var x = float.Parse(coordsSplit[0]);
            var y = float.Parse(coordsSplit[1]);
            var z = float.Parse(coordsSplit[2]);
            var angle = float.Parse(coordsSplit[3]);

            var client = ActiveClients.Get(currentClientId);

            if (client is null)
            {
                return;
            }

            var worldCoords = new WorldCoords(x, y, z, angle);

            var teleportPacket =
                new CharacterDbEntrySerializer(client.CurrentCharacter).GetTeleportByteArray(worldCoords);

            client.MaybeQueueNetworkPacketSend(teleportPacket);
        };
    }
}