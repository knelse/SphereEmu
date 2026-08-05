using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer.Helpers;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Destination picker for admin teleport: continent → poi type → name (coords).
///     Teleport keeps the window open; Close dismisses it.
/// </summary>
public partial class TeleportDestinationWindow : Window
{
    private ItemList? continentList;
    private ItemList? poiTypeList;
    private ItemList? nameList;
    private Button? teleportButton;
    private ushort? targetClientId;

    private readonly List<Continents> continents = [];
    private readonly List<PoiType> poiTypes = [];
    private readonly List<(string Name, WorldCoords Coords)> destinations = [];

    public void OpenForClient(ushort clientId)
    {
        targetClientId = clientId;
        Title = $"Teleport — client {clientId:X4}";
        if (!Visible)
        {
            PopupCentered();
        }

        RefreshContinents();
    }

    public override void _Ready()
    {
        Title = "Teleport";
        Size = new Vector2I(720, 420);
        Unresizable = false;
        Exclusive = false;
        Transient = true;
        Visible = false;
        CloseRequested += Hide;

        var root = new MarginContainer();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("margin_left", 10);
        root.AddThemeConstantOverride("margin_top", 10);
        root.AddThemeConstantOverride("margin_right", 10);
        root.AddThemeConstantOverride("margin_bottom", 10);
        AddChild(root);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        root.AddChild(body);

        var lists = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        lists.AddThemeConstantOverride("separation", 8);
        body.AddChild(lists);

        continentList = MakeLabeledList(lists, "Continent");
        poiTypeList = MakeLabeledList(lists, "POI type");
        nameList = MakeLabeledList(lists, "Name (coords)", expandMore: true);

        continentList.ItemSelected += OnContinentSelected;
        poiTypeList.ItemSelected += OnPoiTypeSelected;
        nameList.ItemSelected += _ => UpdateTeleportEnabled();
        nameList.ItemActivated += _ => TryTeleport();

        var buttons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        buttons.AddThemeConstantOverride("separation", 8);
        body.AddChild(buttons);

        teleportButton = new Button { Text = "Teleport", Disabled = true };
        teleportButton.Pressed += TryTeleport;
        buttons.AddChild(teleportButton);

        var closeButton = new Button { Text = "Close" };
        closeButton.Pressed += Hide;
        buttons.AddChild(closeButton);

        RefreshContinents();
    }

    private static ItemList MakeLabeledList(Control parent, string title, bool expandMore = false)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = expandMore ? 1.6f : 1f
        };
        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);

        column.AddChild(new Label { Text = title });

        var list = new ItemList
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = true
        };
        column.AddChild(list);
        return list;
    }

    private void RefreshContinents()
    {
        if (continentList is null || poiTypeList is null || nameList is null)
        {
            return;
        }

        continents.Clear();
        continentList.Clear();
        poiTypeList.Clear();
        nameList.Clear();
        poiTypes.Clear();
        destinations.Clear();

        foreach (var continent in Enum.GetValues<Continents>())
        {
            if (!HasAnyDestination(continent))
            {
                continue;
            }

            continents.Add(continent);
            continentList.AddItem(continent.ToString());
        }

        UpdateTeleportEnabled();
    }

    private void OnContinentSelected(long index)
    {
        if (poiTypeList is null || nameList is null || index < 0 || index >= continents.Count)
        {
            return;
        }

        var continent = continents[(int)index];
        poiTypes.Clear();
        poiTypeList.Clear();
        nameList.Clear();
        destinations.Clear();

        foreach (var poiType in EnumeratePoiTypes(continent))
        {
            poiTypes.Add(poiType);
            poiTypeList.AddItem(poiType.ToString());
        }

        UpdateTeleportEnabled();
    }

    private void OnPoiTypeSelected(long index)
    {
        if (continentList is null || nameList is null || index < 0 || index >= poiTypes.Count)
        {
            return;
        }

        var continentIndex = GetSelectedIndex(continentList);
        if (continentIndex < 0 || continentIndex >= continents.Count)
        {
            return;
        }

        var continent = continents[continentIndex];
        var poiType = poiTypes[(int)index];

        destinations.Clear();
        nameList.Clear();

        foreach (var (name, coords) in EnumerateDestinations(continent, poiType))
        {
            destinations.Add((name, coords));
            nameList.AddItem($"{name}  [{coords}]");
        }

        UpdateTeleportEnabled();
    }

    private void UpdateTeleportEnabled()
    {
        if (teleportButton is null || nameList is null)
        {
            return;
        }

        var nameIndex = GetSelectedIndex(nameList);
        teleportButton.Disabled = targetClientId is null
                                  || nameIndex < 0
                                  || nameIndex >= destinations.Count;
    }

    private void TryTeleport()
    {
        if (targetClientId is null || nameList is null)
        {
            return;
        }

        var nameIndex = GetSelectedIndex(nameList);
        if (nameIndex < 0 || nameIndex >= destinations.Count)
        {
            return;
        }

        var (name, coords) = destinations[nameIndex];
        var continentIndex = continentList is null ? -1 : GetSelectedIndex(continentList);
        var poiIndex = poiTypeList is null ? -1 : GetSelectedIndex(poiTypeList);
        var continentLabel = continentIndex >= 0 && continentIndex < continents.Count
            ? continents[continentIndex].ToString()
            : "?";
        var poiLabel = poiIndex >= 0 && poiIndex < poiTypes.Count
            ? poiTypes[poiIndex].ToString()
            : "?";
        var label = $"{continentLabel}/{poiLabel}/{name}";

        AdminClientActions.Teleport(targetClientId.Value, coords, label);
        // Keep window open intentionally.
    }

    private static int GetSelectedIndex(ItemList list)
    {
        var selected = list.GetSelectedItems();
        return selected.Length > 0 ? selected[0] : -1;
    }

    private static bool HasAnyDestination(Continents continent)
    {
        if (SavedCoords.TeleportPoints.TryGetValue(continent, out var byType) && byType.Count > 0)
        {
            return byType.Values.Any(d => d.Count > 0);
        }

        return SavedCoords.RespawnPoints.TryGetValue(continent, out var respawns)
               && respawns.Values.Any(city => city.Count > 0);
    }

    private static IEnumerable<PoiType> EnumeratePoiTypes(Continents continent)
    {
        SavedCoords.TeleportPoints.TryGetValue(continent, out var byType);

        if (byType is not null)
        {
            foreach (var poiType in byType.Keys.OrderBy(t => t.ToString()))
            {
                if (byType[poiType].Count > 0)
                {
                    yield return poiType;
                }
            }
        }

        if (SavedCoords.RespawnPoints.TryGetValue(continent, out var respawns)
            && respawns.Values.Any(city => city.Count > 0)
            && (byType is null || !byType.ContainsKey(PoiType.RespawnPoint)))
        {
            yield return PoiType.RespawnPoint;
        }
    }

    private static IEnumerable<(string Name, WorldCoords Coords)> EnumerateDestinations(
        Continents continent, PoiType poiType)
    {
        if (poiType == PoiType.RespawnPoint
            && SavedCoords.RespawnPoints.TryGetValue(continent, out var respawns))
        {
            foreach (var (city, byKarma) in respawns.OrderBy(x => x.Key.ToString()))
            {
                foreach (var (karma, coords) in byKarma.OrderBy(x => x.Key.ToString()))
                {
                    yield return ($"{city}/{karma}", coords);
                }
            }

            yield break;
        }

        if (SavedCoords.TeleportPoints.TryGetValue(continent, out var byType)
            && byType.TryGetValue(poiType, out var named))
        {
            foreach (var (name, coords) in named.OrderBy(x => x.Key))
            {
                yield return (name, coords);
            }
        }
    }
}
