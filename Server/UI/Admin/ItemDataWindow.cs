using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using SphServer.Server.UI.Localization;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Server.UI.Admin;

/// <summary>
/// Catalog browser: item card with the in-game description, ground model, and raw catalog fields
/// </summary>
public partial class ItemDataWindow : Window
{
    private ItemList? typeList;
    private LineEdit? searchEdit;
    private ItemList? itemList;
    private ModelPreview? preview;
    private VBoxContainer? detailsBox;
    private Tree? fieldsTree;
    private Locale locale = Locale.Russian;

    private readonly List<GameObjectType?> types = [];
    private readonly List<SphGameObject> items = [];
    private int shownGameId = -1;
    private string shownModel = "";

    public void SetLocale (Locale newLocale)
    {
        locale = newLocale;
        if (Visible)
        {
            RefreshTypes ();
        }
    }

    public void Open (Locale newLocale)
    {
        locale = newLocale;
        if (!Visible)
        {
            PopupCentered ();
        }

        RefreshTypes ();
    }

    public override void _Ready ()
    {
        Title = "Item Data";
        Size = new Vector2I (1400, 760);
        Unresizable = false;
        Exclusive = false;
        Transient = true;
        Visible = false;
        CloseRequested += Hide;
        VisibilityChanged += () => preview?.SetRendering (Visible);

        var root = new MarginContainer ();
        root.SetAnchorsAndOffsetsPreset (Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride ("margin_left", 10);
        root.AddThemeConstantOverride ("margin_top", 10);
        root.AddThemeConstantOverride ("margin_right", 10);
        root.AddThemeConstantOverride ("margin_bottom", 10);
        AddChild (root);

        var body = new VBoxContainer ();
        body.AddThemeConstantOverride ("separation", 8);
        root.AddChild (body);

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        columns.AddThemeConstantOverride ("separation", 8);
        body.AddChild (columns);

        MakeTypeList (columns);
        MakeItemList (columns);
        preview = new ModelPreview
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.3f,
            CustomMinimumSize = new Vector2 (360, 0)
        };
        columns.AddChild (preview);
        MakeDetails (columns);

        var buttons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End
        };
        var closeButton = new Button { Text = "Close" };
        closeButton.Pressed += Hide;
        buttons.AddChild (closeButton);
        body.AddChild (buttons);
    }

    private void MakeTypeList (Control parent)
    {
        var column = MakeColumn (parent, "Types", 0.55f, 160);
        typeList = new ItemList
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single
        };
        typeList.ItemSelected += _ => RefreshItems ();
        column.AddChild (typeList);
    }

    private void MakeItemList (Control parent)
    {
        var column = MakeColumn (parent, "Items", 0.9f, 240);
        searchEdit = new LineEdit
        {
            PlaceholderText = "Name or game id",
            ClearButtonEnabled = true
        };
        searchEdit.TextChanged += _ => RefreshItems ();
        column.AddChild (searchEdit);
        itemList = new ItemList
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single
        };
        itemList.ItemSelected += OnItemSelected;
        column.AddChild (itemList);
    }

    private void MakeDetails (Control parent)
    {
        var column = MakeColumn (parent, "Item", 0.9f, AdminUiItemDetails.PreferredFrameWidth + 20);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.2f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        column.AddChild (scroll);
        detailsBox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        detailsBox.AddThemeConstantOverride ("separation", 2);
        scroll.AddChild (detailsBox);

        column.AddChild (new Label { Text = "Catalog fields" });
        fieldsTree = new Tree
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Columns = 2,
            ColumnTitlesVisible = false,
            HideRoot = true,
            HideFolding = true,
            SelectMode = Tree.SelectModeEnum.Row
        };
        fieldsTree.SetColumnExpand (0, true);
        fieldsTree.SetColumnExpand (1, true);
        column.AddChild (fieldsTree);
    }

    private static VBoxContainer MakeColumn (Control parent, string title, float stretch, float minWidth)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = stretch,
            CustomMinimumSize = new Vector2 (minWidth, 0)
        };
        column.AddThemeConstantOverride ("separation", 4);
        parent.AddChild (column);
        column.AddChild (new Label { Text = title });
        return column;
    }

    private static IEnumerable<SphGameObject> CatalogItems () =>
        SphObjectDb.GameObjectDataDb.Values.Where (go =>
            go.ObjectKind != GameObjectKind.Monster
            && Enum.GetName (go.GameObjectType)?.StartsWith ("Pref_", StringComparison.Ordinal) == false);

    private void RefreshTypes ()
    {
        if (typeList is null)
        {
            return;
        }

        var previous = SelectedIndex (typeList) is var index and >= 0 && index < types.Count
            ? types[index]
            : null;
        types.Clear ();
        types.Add (null);
        types.AddRange (CatalogItems ()
            .Select (go => go.GameObjectType)
            .Distinct ()
            .OrderBy (type => ItemLocaleText.GameObjectTypeName (type, locale), StringComparer.CurrentCulture)
            .Cast<GameObjectType?> ());

        typeList.Clear ();
        foreach (var type in types)
        {
            typeList.AddItem (type is null ? "All" : ItemLocaleText.GameObjectTypeName (type.Value, locale));
        }

        var selectIndex = Math.Max (0, types.IndexOf (previous));
        typeList.Select (selectIndex);
        RefreshItems ();
    }

    private void RefreshItems ()
    {
        if (typeList is null || itemList is null)
        {
            return;
        }

        var typeIndex = SelectedIndex (typeList);
        var type = typeIndex >= 0 && typeIndex < types.Count ? types[typeIndex] : null;
        var query = searchEdit?.Text.Trim () ?? string.Empty;

        items.Clear ();
        items.AddRange (CatalogItems ()
            .Where (go => type is null || go.GameObjectType == type)
            .Where (go => MatchesQuery (go, query))
            .OrderBy (go => go.GameId));

        itemList.Clear ();
        foreach (var go in items)
        {
            itemList.AddItem (ItemLabel (go));
        }

        if (items.Count == 0)
        {
            return;
        }

        var selectIndex = Math.Max (0, items.FindIndex (go => go.GameId == shownGameId));
        itemList.Select (selectIndex);
        if (selectIndex == 0)
        {
            itemList.GetVScrollBar ().Value = 0;
        }
        else
        {
            // The list has not been laid out yet, so scrolling now lands on stale row heights
            Callable.From (itemList.EnsureCurrentIsVisible).CallDeferred ();
        }
        ShowItem (items[selectIndex]);
    }

    private bool MatchesQuery (SphGameObject go, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        return go.GameId.ToString (CultureInfo.InvariantCulture).Contains (query, StringComparison.Ordinal)
               || ItemLocaleText.CatalogName (go, locale).Contains (query, StringComparison.CurrentCultureIgnoreCase);
    }

    private string ItemLabel (SphGameObject go)
    {
        var name = ItemLocaleText.CatalogName (go, locale);
        return go.IsTierVisible () && go.Tier > 0
            ? $"{go.GameId}  {name}  T{go.Tier}"
            : $"{go.GameId}  {name}";
    }

    private void OnItemSelected (long index)
    {
        if (index < 0 || index >= items.Count)
        {
            return;
        }

        ShowItem (items[(int) index]);
    }

    private void ShowItem (SphGameObject go)
    {
        shownGameId = go.GameId;
        if (detailsBox is not null)
        {
            // Same card as the inventory tooltip, including the in-game description
            AdminUiItemDetails.Fill (detailsBox, ItemDbEntry.CreateFromGameObject (go), null, locale);
        }

        FillFields (go);

        var model = GroundModelFile (go.ModelNameGround);
        if (model == shownModel && preview?.HasModel == true)
        {
            return;
        }

        shownModel = model;
        preview?.LoadModel (model);
    }

    /// <summary>
    /// Wearables carry a <c>@xy@</c> wear code in front of the model file name
    /// </summary>
    private static string GroundModelFile (string? modelName)
    {
        var trimmed = modelName?.Trim () ?? string.Empty;
        if (trimmed.StartsWith ('@'))
        {
            var close = trimmed.IndexOf ('@', 1);
            if (close > 0)
            {
                return trimmed[(close + 1)..];
            }
        }

        return trimmed;
    }

    private void FillFields (SphGameObject go)
    {
        if (fieldsTree is null)
        {
            return;
        }

        fieldsTree.Clear ();
        var root = fieldsTree.CreateItem ();
        foreach (var property in typeof (SphGameObject).GetProperties ())
        {
            if (property.GetIndexParameters ().Length > 0)
            {
                continue;
            }

            var value = property.GetValue (go);
            if (value is IEnumerable and not string)
            {
                continue;
            }

            var row = fieldsTree.CreateItem (root);
            row.SetText (0, property.Name);
            row.SetText (1, Convert.ToString (value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    private static int SelectedIndex (ItemList list)
    {
        var selected = list.GetSelectedItems ();
        return selected.Length > 0 ? selected[0] : -1;
    }
}
