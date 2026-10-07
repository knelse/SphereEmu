using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using SphServer.Server.UI.Localization;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Catalog browser: localized mob list, orbiting model preview, stats for levels 1-350.
/// </summary>
public partial class MobDataWindow : Window
{
    private const int MaxLevel = 350;
    private const int IconPx = 18;
    private const float DefaultYaw = Mathf.Pi;
    private const float DefaultPitch = 0.2f;

    private ItemList? mobList;
    private OptionButton? animationSelect;
    private Label? previewStatus;
    private Tree? statsTree;
    private SubViewport? previewViewport;
    private Node3D? modelHolder;
    private MeshInstance3D? floorGrid;
    private Camera3D? camera;
    private Locale locale = Locale.Russian;

    private readonly List<SphGameObject> mobs = [];
    private readonly List<AnimationPlayer> animationPlayers = [];
    private Node3D? modelInstance;
    private int shownGameId = -1;
    private string playingClip = "";
    private bool suppressAnimationCallback;

    private float yaw = DefaultYaw;
    private float pitch = DefaultPitch;
    private float distance = 5f;
    private Vector3 focus = Vector3.Zero;
    private bool orbiting;
    private bool zooming;
    private bool panning;

    public void SetLocale(Locale newLocale)
    {
        locale = newLocale;
        if (Visible)
        {
            RefreshList(keepSelection: true);
        }
    }

    public void Open(Locale newLocale)
    {
        locale = newLocale;
        if (!Visible)
        {
            PopupCentered();
        }

        RefreshList(keepSelection: mobList?.GetSelectedItems().Length > 0);
    }

    public override void _Ready()
    {
        Title = "Mob Data";
        Size = new Vector2I(1280, 720);
        Unresizable = false;
        Exclusive = false;
        Transient = true;
        Visible = false;
        CloseRequested += Hide;
        VisibilityChanged += OnVisibilityChanged;

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

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        columns.AddThemeConstantOverride("separation", 8);
        body.AddChild(columns);

        mobList = MakeMobList(columns);
        MakePreview(columns);
        MakeStats(columns);

        var buttons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End
        };
        var closeButton = new Button { Text = "Close" };
        closeButton.Pressed += Hide;
        buttons.AddChild(closeButton);
        body.AddChild(buttons);

        BuildPreviewWorld();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!orbiting && !zooming && !panning)
        {
            return;
        }

        if (inputEvent is InputEventMouseButton { Pressed: false } released)
        {
            if (released.ButtonIndex == MouseButton.Left)
            {
                orbiting = false;
            }

            if (released.ButtonIndex == MouseButton.Right)
            {
                zooming = false;
            }

            if (released.ButtonIndex == MouseButton.Middle)
            {
                panning = false;
            }

            return;
        }

        if (inputEvent is not InputEventMouseMotion motion)
        {
            return;
        }

        if (orbiting)
        {
            yaw -= motion.Relative.X * 0.01f;
            pitch = Mathf.Clamp(pitch + motion.Relative.Y * 0.01f, -1.2f, 1.2f);
        }

        if (zooming)
        {
            distance = Mathf.Clamp(distance * (1f + motion.Relative.Y * 0.01f), 0.35f, 80f);
        }

        if (panning)
        {
            Pan(motion.Relative);
        }

        UpdateCamera();
        GetViewport().SetInputAsHandled();
    }

    private void OnVisibilityChanged()
    {
        if (previewViewport is null)
        {
            return;
        }

        previewViewport.RenderTargetUpdateMode = Visible
            ? SubViewport.UpdateMode.Always
            : SubViewport.UpdateMode.Disabled;
    }

    private ItemList MakeMobList(Control parent)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.85f,
            CustomMinimumSize = new Vector2(220, 0)
        };
        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);
        column.AddChild(new Label { Text = "Mobs" });
        var list = new ItemList
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single
        };
        list.ItemSelected += OnMobSelected;
        column.AddChild(list);
        return list;
    }

    private Control MakePreview(Control parent)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.4f,
            CustomMinimumSize = new Vector2(360, 0)
        };
        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);

        var viewportContainer = new SubViewportContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        viewportContainer.GuiInput += OnPreviewGuiInput;
        column.AddChild(viewportContainer);

        previewViewport = new SubViewport
        {
            OwnWorld3D = true,
            HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Size = new Vector2I(640, 480)
        };
        viewportContainer.AddChild(previewViewport);

        var animationRow = new HBoxContainer();
        animationRow.AddThemeConstantOverride("separation", 8);
        animationRow.AddChild(new Label { Text = "Animation" });
        animationSelect = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        animationSelect.ItemSelected += _ =>
        {
            if (!suppressAnimationCallback)
            {
                PlaySelectedAnimation();
            }
        };
        animationRow.AddChild(animationSelect);
        column.AddChild(animationRow);

        previewStatus = new Label();
        column.AddChild(previewStatus);
        return column;
    }

    private void MakeStats(Control parent)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.9f,
            CustomMinimumSize = new Vector2(330, 0)
        };
        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);
        column.AddChild(new Label { Text = "Stats" });

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 0);
        AddHeaderIcon(header, AdminUiAtlas.TitleIcon);
        AddHeaderIcon(header, AdminUiAtlas.HpIcon);
        AddHeaderIcon(header, AdminUiAtlas.PAtkIcon);
        AddHeaderIcon(header, AdminUiAtlas.MAtkIcon);
        AddHeaderIcon(header, AdminUiAtlas.PDefIcon);
        AddHeaderIcon(header, AdminUiAtlas.MDefIcon);
        header.AddChild(new Control { CustomMinimumSize = new Vector2(14, 0) });
        column.AddChild(header);

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
            statsTree.SetColumnExpand(columnIndex, true);
            statsTree.SetColumnCustomMinimumWidth(columnIndex, 48);
        }

        column.AddChild(statsTree);
    }

    private static void AddHeaderIcon(Control parent, Texture2D? texture)
    {
        var slot = new CenterContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(48, IconPx)
        };
        slot.AddChild(new TextureRect
        {
            Texture = texture,
            CustomMinimumSize = new Vector2(IconPx, IconPx),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest
        });
        parent.AddChild(slot);
    }

    private void BuildPreviewWorld()
    {
        if (previewViewport is null)
        {
            return;
        }

        var world = new Node3D { Name = "PreviewWorld" };
        previewViewport.AddChild(world);

        var environment = new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.15f, 0.15f, 0.17f),
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.55f, 0.58f),
            AmbientLightEnergy = 0.85f
        };
        world.AddChild(new WorldEnvironment { Environment = environment });

        var key = new DirectionalLight3D
        {
            Rotation = new Vector3(-0.9f, 0.6f, 0f),
            ShadowEnabled = true
        };
        world.AddChild(key);
        world.AddChild(new DirectionalLight3D
        {
            Rotation = new Vector3(-0.2f, 2.6f, 0f),
            LightEnergy = 0.35f
        });

        floorGrid = new MeshInstance3D
        {
            Mesh = BuildGridMesh(),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.38f, 0.38f, 0.42f)
            }
        };
        world.AddChild(floorGrid);

        modelHolder = new Node3D { Name = "Model" };
        world.AddChild(modelHolder);

        camera = new Camera3D
        {
            Current = true,
            Fov = 50f,
            Near = 0.05f,
            Far = 200f
        };
        world.AddChild(camera);
        UpdateCamera();
    }

    private static ImmediateMesh BuildGridMesh()
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (var i = -4; i <= 4; i++)
        {
            mesh.SurfaceAddVertex(new Vector3(i, 0f, -4f));
            mesh.SurfaceAddVertex(new Vector3(i, 0f, 4f));
            mesh.SurfaceAddVertex(new Vector3(-4f, 0f, i));
            mesh.SurfaceAddVertex(new Vector3(4f, 0f, i));
        }

        mesh.SurfaceEnd();
        return mesh;
    }

    private void OnPreviewGuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
            {
                distance = Mathf.Max(0.35f, distance * 0.9f);
            }
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
            {
                distance = Mathf.Min(80f, distance * 1.1f);
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                orbiting = button.Pressed;
            }
            else if (button.ButtonIndex == MouseButton.Right)
            {
                zooming = button.Pressed;
            }
            else if (button.ButtonIndex == MouseButton.Middle)
            {
                panning = button.Pressed;
            }
            else
            {
                return;
            }

            UpdateCamera();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent is not InputEventMouseMotion motion)
        {
            return;
        }

        if (orbiting)
        {
            yaw -= motion.Relative.X * 0.01f;
            pitch = Mathf.Clamp(pitch + motion.Relative.Y * 0.01f, -1.2f, 1.2f);
        }
        else if (zooming)
        {
            distance = Mathf.Clamp(distance * (1f + motion.Relative.Y * 0.01f), 0.35f, 80f);
        }
        else if (panning)
        {
            Pan(motion.Relative);
        }
        else
        {
            return;
        }

        UpdateCamera();
        GetViewport().SetInputAsHandled();
    }

    private void UpdateCamera()
    {
        if (camera is null)
        {
            return;
        }

        var horizontal = distance * Mathf.Cos(pitch);
        camera.Position = focus + new Vector3(
            horizontal * Mathf.Sin(yaw),
            distance * Mathf.Sin(pitch),
            horizontal * Mathf.Cos(yaw));
        camera.LookAt(focus, Vector3.Up);
    }

    private void Pan(Vector2 relative)
    {
        if (camera is null)
        {
            return;
        }

        var basis = camera.GlobalTransform.Basis;
        var scale = distance * 0.0015f;
        focus += (-basis.X * relative.X + basis.Y * relative.Y) * scale;
    }

    private void RefreshList(bool keepSelection)
    {
        if (mobList is null)
        {
            return;
        }

        var previousId = shownGameId;
        mobs.Clear();
        mobs.AddRange(SphObjectDb.GameObjectDataDb.Values
            .Where(go => go.ObjectKind == GameObjectKind.Monster)
            .OrderBy(go => go.GameId));

        mobList.Clear();
        foreach (var mob in mobs)
        {
            var name = ItemLocaleText.CatalogName(mob, locale);
            var index = mobList.ItemCount;
            mobList.AddItem($"{mob.GameId}  {name}");
            mobList.SetItemMetadata(index, mob.GameId);
        }

        if (mobs.Count == 0)
        {
            return;
        }

        var selectIndex = 0;
        if (keepSelection && previousId >= 0)
        {
            var found = mobs.FindIndex(go => go.GameId == previousId);
            if (found >= 0)
            {
                selectIndex = found;
            }
        }

        mobList.Select(selectIndex);
        ShowMonster(mobs[selectIndex]);
    }

    private void OnMobSelected(long index)
    {
        if (mobList is null || index < 0 || index >= mobs.Count)
        {
            return;
        }

        ShowMonster(mobs[(int)index]);
    }

    private void ShowMonster(SphGameObject mob)
    {
        FillStats(mob);
        if (mob.GameId == shownGameId && modelInstance is not null)
        {
            return;
        }

        shownGameId = mob.GameId;
        LoadModel(mob.ModelNameGround);
    }

    private void FillStats(SphGameObject mob)
    {
        if (statsTree is null)
        {
            return;
        }

        var data = new SphMonsterData(mob);
        statsTree.Clear();
        var root = statsTree.CreateItem();
        for (var level = 1; level <= MaxLevel; level++)
        {
            var row = statsTree.CreateItem(root);
            // Stored attack is negative. Show magnitude, same as the character panel.
            row.SetText(0, level.ToString(CultureInfo.InvariantCulture));
            row.SetText(1, (level * data.HpPerLevel).ToString(CultureInfo.InvariantCulture));
            row.SetText(2, (-(level * data.PAtkPerLevel)).ToString(CultureInfo.InvariantCulture));
            row.SetText(3, (-(level * data.MAtkPerLevel)).ToString(CultureInfo.InvariantCulture));
            row.SetText(4, (level * data.PDefPerLevel).ToString(CultureInfo.InvariantCulture));
            row.SetText(5, (level * data.MDefPerLevel).ToString(CultureInfo.InvariantCulture));
            for (var columnIndex = 0; columnIndex < 6; columnIndex++)
            {
                row.SetTextAlignment(columnIndex, HorizontalAlignment.Center);
            }
        }
    }

    private void LoadModel(string? modelName)
    {
        ClearModel();
        if (modelHolder is null)
        {
            return;
        }

        var trimmed = modelName?.Trim() ?? string.Empty;
        var path = string.IsNullOrEmpty(trimmed) ? null : GlbModelPaths.Resolve(trimmed);
        if (path is null)
        {
            if (previewStatus is not null)
            {
                previewStatus.Text = string.IsNullOrEmpty(trimmed)
                    ? "No model"
                    : $"Model not found: {trimmed}";
            }

            FillAnimations([]);
            return;
        }

        var packed = ResourceLoader.Load<PackedScene>(path);
        if (packed?.Instantiate() is not Node3D root)
        {
            if (previewStatus is not null)
            {
                previewStatus.Text = $"Failed to load {trimmed}";
            }

            FillAnimations([]);
            return;
        }

        modelInstance = root;
        modelHolder.AddChild(root);
        CollectAnimationPlayers(root, animationPlayers);
        foreach (var player in animationPlayers)
        {
            player.AnimationFinished += OnAnimationFinished;
        }

        FrameModel(root);
        FillAnimations(animationPlayers);
        if (previewStatus is not null)
        {
            previewStatus.Text = trimmed;
        }
    }

    private void ClearModel()
    {
        foreach (var player in animationPlayers)
        {
            if (GodotObject.IsInstanceValid(player))
            {
                player.AnimationFinished -= OnAnimationFinished;
            }
        }

        animationPlayers.Clear();
        if (modelInstance is not null && GodotObject.IsInstanceValid(modelInstance))
        {
            modelInstance.QueueFree();
        }

        modelInstance = null;
        playingClip = "";
    }

    private static void CollectAnimationPlayers(Node node, List<AnimationPlayer> into)
    {
        if (node is AnimationPlayer player)
        {
            into.Add(player);
        }

        foreach (var child in node.GetChildren())
        {
            CollectAnimationPlayers(child, into);
        }
    }

    private void FrameModel(Node3D root)
    {
        yaw = DefaultYaw;
        pitch = DefaultPitch;
        focus = Vector3.Zero;
        if (!TryGetCombinedAabb(root, out var bounds))
        {
            distance = 5f;
            UpdateCamera();
            return;
        }

        root.Position -= bounds.GetCenter();
        if (floorGrid is not null)
        {
            floorGrid.Position = new Vector3(0f, -bounds.Size.Y * 0.5f, 0f);
        }

        distance = Mathf.Clamp(bounds.Size.Length() * 1.45f, 1.2f, 40f);
        UpdateCamera();
    }

    private static bool TryGetCombinedAabb(Node3D root, out Aabb bounds)
    {
        bounds = default;
        var any = false;
        MergeAabb(root, root, ref bounds, ref any);
        return any;
    }

    private static void MergeAabb(Node3D node, Node3D space, ref Aabb bounds, ref bool any)
    {
        if (node is MeshInstance3D { Mesh: not null } mesh)
        {
            var transformed = TransformAabb(
                mesh.GetAabb(),
                space.GlobalTransform.AffineInverse() * mesh.GlobalTransform);
            bounds = any ? bounds.Merge(transformed) : transformed;
            any = true;
        }

        foreach (var child in node.GetChildren())
        {
            if (child is Node3D child3D)
            {
                MergeAabb(child3D, space, ref bounds, ref any);
            }
        }
    }

    private static Aabb TransformAabb(Aabb aabb, Transform3D transform)
    {
        var origin = aabb.Position;
        var size = aabb.Size;
        Vector3 Corner(int index) => transform * (origin + new Vector3(
            (index & 1) == 0 ? 0f : size.X,
            (index & 2) == 0 ? 0f : size.Y,
            (index & 4) == 0 ? 0f : size.Z));

        var merged = new Aabb(Corner(0), Vector3.Zero);
        for (var index = 1; index < 8; index++)
        {
            merged = merged.Expand(Corner(index));
        }

        return merged;
    }

    private void FillAnimations(List<AnimationPlayer> players)
    {
        if (animationSelect is null)
        {
            return;
        }

        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in players)
        {
            foreach (var name in player.GetAnimationList())
            {
                if (!string.IsNullOrEmpty(name) && !name.Equals("RESET", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }
        }

        var idleClip = names.FirstOrDefault(name => name.EndsWith("_00", StringComparison.OrdinalIgnoreCase)
                          || name.Equals("00", StringComparison.OrdinalIgnoreCase))
                      ?? names.FirstOrDefault(IsIdleName)
                      ?? "";
        suppressAnimationCallback = true;
        animationSelect.Clear();
        animationSelect.AddItem("Idle");
        animationSelect.SetItemMetadata(0, idleClip);
        var itemIndex = 1;
        foreach (var name in names)
        {
            if (IsIdleName(name))
            {
                continue;
            }

            animationSelect.AddItem(name);
            animationSelect.SetItemMetadata(itemIndex, name);
            itemIndex++;
        }

        animationSelect.Select(0);
        suppressAnimationCallback = false;
        PlaySelectedAnimation();
    }

    private static bool IsIdleName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower is "idle" or "stay" or "stand" or "00"
               || lower.Contains("idle")
               || lower.EndsWith("_00", StringComparison.Ordinal);
    }

    private void PlaySelectedAnimation()
    {
        if (animationSelect is null || animationSelect.Selected < 0)
        {
            return;
        }

        playingClip = animationSelect.GetItemMetadata(animationSelect.Selected).AsString();
        foreach (var player in animationPlayers)
        {
            if (!GodotObject.IsInstanceValid(player))
            {
                continue;
            }

            if (string.IsNullOrEmpty(playingClip))
            {
                if (player.HasAnimation("RESET"))
                {
                    player.Play("RESET");
                }
                else
                {
                    player.Stop();
                }

                continue;
            }

            if (player.HasAnimation(playingClip))
            {
                player.Play(playingClip);
            }
        }
    }

    private void OnAnimationFinished(StringName name)
    {
        if (string.IsNullOrEmpty(playingClip) || !name.ToString().Equals(playingClip, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var player in animationPlayers)
        {
            if (GodotObject.IsInstanceValid(player) && player.HasAnimation(playingClip))
            {
                player.Play(playingClip);
            }
        }
    }
}
