using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using SphServer.Server.UI.Localization;

namespace SphServer.Server.UI.Admin;

/// <summary>
/// Stats cover levels 1-350
/// </summary>
public partial class MobDataWindow : Window
{
    private const int MaxLevel = 350;
    private const int IconPx = 18;

    private ItemList? mobList;
    private ModelPreview? preview;
    private Tree? statsTree;
    private Locale locale = Locale.Russian;

    private readonly List<SphGameObject> mobs = [];
    private int shownGameId = -1;

    public void SetLocale (Locale newLocale)
    {
        locale = newLocale;
        if (Visible)
        {
            RefreshList (keepSelection: true);
        }
    }

    public void Open (Locale newLocale)
    {
        locale = newLocale;
        if (!Visible)
        {
            PopupCentered ();
        }

        RefreshList (keepSelection: mobList?.GetSelectedItems ().Length > 0);
    }

    public override void _Ready ()
    {
        Title = "Mob Data";
        Size = new Vector2I (1280, 720);
        Unresizable = false;
        Exclusive = false;
        Transient = true;
        Visible = false;
        CloseRequested += Hide;
        VisibilityChanged += OnVisibilityChanged;

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

        mobList = MakeMobList (columns);
        MakePreview (columns);
        MakeStats (columns);

        var buttons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End
        };
        var closeButton = new Button { Text = "Close" };
        closeButton.Pressed += Hide;
        buttons.AddChild (closeButton);
        body.AddChild (buttons);
    }

    private void OnVisibilityChanged ()
    {
        preview?.SetRendering (Visible);
    }

    private ItemList MakeMobList (Control parent)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.85f,
            CustomMinimumSize = new Vector2 (220, 0)
        };
        column.AddThemeConstantOverride ("separation", 4);
        parent.AddChild (column);
        column.AddChild (new Label { Text = "Mobs" });
        var list = new ItemList
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single
        };
        list.ItemSelected += OnMobSelected;
        column.AddChild (list);
        return list;
    }

    private void MakePreview (Control parent)
    {
        preview = new ModelPreview
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.4f,
            CustomMinimumSize = new Vector2 (360, 0)
        };
        parent.AddChild (preview);
    }

    private void MakeStats (Control parent)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.9f,
            CustomMinimumSize = new Vector2 (330, 0)
        };
        column.AddThemeConstantOverride ("separation", 4);
        parent.AddChild (column);
        column.AddChild (new Label { Text = "Stats" });

        var header = new HBoxContainer ();
        header.AddThemeConstantOverride ("separation", 0);
        AddHeaderIcon (header, AdminUiAtlas.TitleIcon);
        AddHeaderIcon (header, AdminUiAtlas.HpIcon);
        AddHeaderIcon (header, AdminUiAtlas.PAtkIcon);
        AddHeaderIcon (header, AdminUiAtlas.MAtkIcon);
        AddHeaderIcon (header, AdminUiAtlas.PDefIcon);
        AddHeaderIcon (header, AdminUiAtlas.MDefIcon);
        header.AddChild (new Control { CustomMinimumSize = new Vector2 (14, 0) });
        column.AddChild (header);

        statsTree = new Tree
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Columns = 6,
            ColumnTitlesVisible = false,
            HideRoot = true,
            HideFolding = true
        };
        for (var columnIndex = 0; columnIndex < 6; columnIndex++)
        {
            statsTree.SetColumnExpand (columnIndex, true);
            statsTree.SetColumnCustomMinimumWidth (columnIndex, 48);
        }

        column.AddChild (statsTree);
    }

    private static void AddHeaderIcon (Control parent, Texture2D? texture)
    {
        var slot = new CenterContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2 (48, IconPx)
        };
        slot.AddChild (new TextureRect
        {
            Texture = texture,
            CustomMinimumSize = new Vector2 (IconPx, IconPx),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest
        });
        parent.AddChild (slot);
    }

    private void RefreshList (bool keepSelection)
    {
        if (mobList is null)
        {
            return;
        }

        var previousId = shownGameId;
        mobs.Clear ();
        mobs.AddRange (SphObjectDb.GameObjectDataDb.Values
            .Where (go => go.ObjectKind == GameObjectKind.Monster)
            .OrderBy (go => go.GameId));

        mobList.Clear ();
        foreach (var mob in mobs)
        {
            var name = ItemLocaleText.CatalogName (mob, locale);
            var index = mobList.ItemCount;
            mobList.AddItem ($"{mob.GameId}  {name}");
            mobList.SetItemMetadata (index, mob.GameId);
        }

        if (mobs.Count == 0)
        {
            return;
        }

        var selectIndex = 0;
        if (keepSelection && previousId >= 0)
        {
            var found = mobs.FindIndex (go => go.GameId == previousId);
            if (found >= 0)
            {
                selectIndex = found;
            }
        }

        mobList.Select (selectIndex);
        ShowMonster (mobs[selectIndex]);
    }

    private void OnMobSelected (long index)
    {
        if (mobList is null || index < 0 || index >= mobs.Count)
        {
            return;
        }

        ShowMonster (mobs[(int) index]);
    }

    private void ShowMonster (SphGameObject mob)
    {
        FillStats (mob);
        if (mob.GameId == shownGameId && preview?.HasModel == true)
        {
            return;
        }

        shownGameId = mob.GameId;
        preview?.LoadModel (mob.ModelNameGround);
    }

    private void FillStats (SphGameObject mob)
    {
        if (statsTree is null)
        {
            return;
        }

        var data = new SphMonsterData (mob);
        statsTree.Clear ();
        var root = statsTree.CreateItem ();
        for (var level = 1; level <= MaxLevel; level++)
        {
            var row = statsTree.CreateItem (root);
            // Stored attack is negative; the panel shows the magnitude
            row.SetText (0, level.ToString (CultureInfo.InvariantCulture));
            row.SetText (1, (level * data.HpPerLevel).ToString (CultureInfo.InvariantCulture));
            row.SetText (2, (-(level * data.PAtkPerLevel)).ToString (CultureInfo.InvariantCulture));
            row.SetText (3, (-(level * data.MAtkPerLevel)).ToString (CultureInfo.InvariantCulture));
            row.SetText (4, (level * data.PDefPerLevel).ToString (CultureInfo.InvariantCulture));
            row.SetText (5, (level * data.MDefPerLevel).ToString (CultureInfo.InvariantCulture));
            for (var columnIndex = 0; columnIndex < 6; columnIndex++)
            {
                row.SetTextAlignment (columnIndex, HorizontalAlignment.Center);
            }
        }
    }
}
