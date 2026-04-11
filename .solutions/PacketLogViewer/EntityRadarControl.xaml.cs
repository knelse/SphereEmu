using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PacketLogViewer.Models.PacketAnalyzeData;
using SphServer.Helpers;

namespace PacketLogViewer;

public partial class EntityRadarControl
{
    public const double RadarRadiusMeters = 200;

    private IReadOnlyList<PacketAnalyzeData> _items = Array.Empty<PacketAnalyzeData>();
    private double _clientX;
    private double _clientZ;
    /// <summary>Radians: 0 = north (+Z), counter-clockwise positive; east = -π/2.</summary>
    private double _clientTurn;
    private bool _hasClientPosition;

    private double _lastRingsLayoutWidth = double.NaN;
    private double _lastRingsLayoutHeight = double.NaN;

    private readonly Dictionary<int, (Shape Shape, MarkerVisualKind Kind)> _markersByEntityId = new();

    public EntityRadarControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
    }

    public void SetEntities(IReadOnlyList<PacketAnalyzeData> items, double clientX, double clientZ, double clientTurnRad,
        bool hasClientPosition)
    {
        _items = items;
        _clientX = clientX;
        _clientZ = clientZ;
        _clientTurn = clientTurnRad;
        _hasClientPosition = hasClientPosition;
        Redraw();
    }

    private void Redraw()
    {
        PlaceholderText.Visibility = _hasClientPosition ? Visibility.Collapsed : Visibility.Visible;
        if (!_hasClientPosition || ActualWidth < 4 || ActualHeight < 4)
        {
            ClearEntityMarkers();
            RingsCanvas.Children.Clear();
            _lastRingsLayoutWidth = double.NaN;
            return;
        }

        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var half = Math.Min(ActualWidth, ActualHeight) / 2;
        var pxPerMeter = half / RadarRadiusMeters;

        if (double.IsNaN(_lastRingsLayoutWidth) ||
            Math.Abs(_lastRingsLayoutWidth - ActualWidth) > 0.5 ||
            Math.Abs(_lastRingsLayoutHeight - ActualHeight) > 0.5)
        {
            RingsCanvas.Children.Clear();
            DrawRangeRings(cx, cy, pxPerMeter);
            var clientMarker = CreateClientMarker();
            clientMarker.IsHitTestVisible = false;
            PositionShape(clientMarker, cx, cy);
            RingsCanvas.Children.Add(clientMarker);
            _lastRingsLayoutWidth = ActualWidth;
            _lastRingsLayoutHeight = ActualHeight;
        }

        var visibleIds = new HashSet<int>();
        foreach (var pad in _items)
        {
            if (!TryGetMapPosition(pad, out var ex, out var ez))
            {
                continue;
            }

            var dx = ex - _clientX;
            var dz = ez - _clientZ;
            var dist = Math.Sqrt(dx * dx + dz * dz);
            if (dist > RadarRadiusMeters)
            {
                continue;
            }

            WorldOffsetToRadarScreen(dx, dz, _clientTurn, cx, cy, pxPerMeter, out var px, out var py);

            visibleIds.Add(pad.Id);
            UpsertEntityMarker(pad, px, py);
        }

        foreach (var id in _markersByEntityId.Keys.Where(id => !visibleIds.Contains(id)).ToList())
        {
            RemoveMarker(id);
        }
    }

    private void UpsertEntityMarker(PacketAnalyzeData pad, double px, double py)
    {
        var kind = GetMarkerVisualKind(pad);
        if (_markersByEntityId.TryGetValue(pad.Id, out var existing) && existing.Kind == kind)
        {
            PositionShape(existing.Shape, px, py);
            var tip = pad.DisplayValue;
            if (existing.Shape.ToolTip as string != tip)
            {
                existing.Shape.ToolTip = tip;
            }

            return;
        }

        if (_markersByEntityId.TryGetValue(pad.Id, out var old))
        {
            EntitiesCanvas.Children.Remove(old.Shape);
            _markersByEntityId.Remove(pad.Id);
        }

        var shape = CreateMarkerShape(pad);
        shape.ToolTip = pad.DisplayValue;
        ToolTipService.SetInitialShowDelay(shape, 0);
        ToolTipService.SetBetweenShowDelay(shape, 0);
        PositionShape(shape, px, py);
        EntitiesCanvas.Children.Add(shape);
        _markersByEntityId[pad.Id] = (shape, kind);
    }

    private void RemoveMarker(int id)
    {
        if (!_markersByEntityId.TryGetValue(id, out var entry))
        {
            return;
        }

        EntitiesCanvas.Children.Remove(entry.Shape);
        _markersByEntityId.Remove(id);
    }

    private void ClearEntityMarkers()
    {
        EntitiesCanvas.Children.Clear();
        _markersByEntityId.Clear();
    }

    /// <summary>
    /// World offset (dx,dz) in meters; <paramref name="turnRad"/>: 0 = north (+Z), CCW+, east = -π/2.
    /// Maps so player forward points to the top of the radar (negative canvas Y).
    /// </summary>
    private static void WorldOffsetToRadarScreen(double dx, double dz, double turnRad, double cx, double cy,
        double pxPerMeter, out double px, out double py)
    {
        var sinT = Math.Sin(turnRad);
        var cosT = Math.Cos(turnRad);
        var localForward = -dx * sinT + dz * cosT;
        var localRight = dx * cosT + dz * sinT;
        px = cx + localRight * pxPerMeter;
        py = cy - localForward * pxPerMeter;
    }

    private void DrawRangeRings(double cx, double cy, double pxPerMeter)
    {
        foreach (var r in new[] { 50, 100, 150, 200 })
        {
            var d = 2 * r * pxPerMeter;
            var ring = new Ellipse
            {
                Width = d,
                Height = d,
                Stroke = new SolidColorBrush(Color.FromArgb(80, 160, 160, 160)),
                StrokeThickness = 1,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, cx - d / 2);
            Canvas.SetTop(ring, cy - d / 2);
            RingsCanvas.Children.Add(ring);
        }
    }

    private static void PositionShape(Shape shape, double centerX, double centerY)
    {
        Canvas.SetLeft(shape, centerX - shape.Width / 2);
        Canvas.SetTop(shape, centerY - shape.Height / 2);
    }

    private static Shape CreateClientMarker()
    {
        return new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };
    }

    private enum MarkerVisualKind
    {
        Character,
        Chest,
        Npc,
        Monster,
        DoorTeleport,
        Item,
        WorldObject,
        Default
    }

    private static MarkerVisualKind GetMarkerVisualKind(PacketAnalyzeData pad)
    {
        var ot = pad.ObjectType;
        if (pad is CharacterPacket)
        {
            return MarkerVisualKind.Character;
        }

        if (IsChest(ot))
        {
            return MarkerVisualKind.Chest;
        }

        if (IsNpc(ot) || pad is NpcTradePacket)
        {
            return MarkerVisualKind.Npc;
        }

        if (ot is ObjectType.Monster or ObjectType.MonsterFlyer)
        {
            return MarkerVisualKind.Monster;
        }

        if (IsDoorOrTeleport(ot))
        {
            return MarkerVisualKind.DoorTeleport;
        }

        if (pad is ItemPacket)
        {
            return MarkerVisualKind.Item;
        }

        if (pad is WorldObject)
        {
            return MarkerVisualKind.WorldObject;
        }

        return MarkerVisualKind.Default;
    }

    private static Shape CreateMarkerShape(PacketAnalyzeData pad)
    {
        var ot = pad.ObjectType;

        if (pad is CharacterPacket)
        {
            return EllipseMarker(Brushes.DeepSkyBlue, 10);
        }

        if (IsChest(ot))
        {
            return new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Gold,
                Stroke = Brushes.DarkGoldenrod,
                StrokeThickness = 1
            };
        }

        if (IsNpc(ot) || pad is NpcTradePacket)
        {
            return TriangleMarker(Brushes.LimeGreen, Brushes.DarkGreen, 12);
        }

        if (ot is ObjectType.Monster or ObjectType.MonsterFlyer)
        {
            return EllipseMarker(Brushes.OrangeRed, 10);
        }

        if (IsDoorOrTeleport(ot))
        {
            return DiamondMarker(Brushes.Cyan, Brushes.DarkCyan, 12);
        }

        if (pad is ItemPacket)
        {
            return EllipseMarker(Brushes.SandyBrown, 7);
        }

        if (pad is WorldObject)
        {
            return EllipseMarker(Brushes.Gray, 8);
        }

        return EllipseMarker(Brushes.LightGray, 7);
    }

    private static Shape EllipseMarker(Brush fill, double size)
    {
        return new Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            Stroke = Brushes.Black,
            StrokeThickness = 1
        };
    }

    private static Polygon TriangleMarker(Brush fill, Brush stroke, double size)
    {
        var h = size;
        var w = size;
        return new Polygon
        {
            Points = new PointCollection(new[]
            {
                new Point(w / 2, 0),
                new Point(w, h),
                new Point(0, h)
            }),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 1,
            Width = w,
            Height = h
        };
    }

    private static Polygon DiamondMarker(Brush fill, Brush stroke, double size)
    {
        var h = size;
        var w = size;
        return new Polygon
        {
            Points = new PointCollection(new[]
            {
                new Point(w / 2, 0),
                new Point(w, h / 2),
                new Point(w / 2, h),
                new Point(0, h / 2)
            }),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 1,
            Width = w,
            Height = h
        };
    }

    private static bool IsChest(ObjectType o) =>
        o is ObjectType.Chest or ObjectType.ChestInDungeon or ObjectType.CastleChest;

    private static bool IsNpc(ObjectType o) => o switch
    {
        ObjectType.NpcTradeRandomName or ObjectType.NpcQuestTitle or ObjectType.NpcQuestKarma
            or ObjectType.NpcQuestDegree or ObjectType.NpcGuide or ObjectType.NpcTrade
            or ObjectType.NpcGuilder or ObjectType.NpcBanker or ObjectType.NpcTournament => true,
        _ => false
    };

    private static bool IsDoorOrTeleport(ObjectType o) => o switch
    {
        ObjectType.DoorEntrance or ObjectType.DoorExit or ObjectType.TeleportWithTarget
            or ObjectType.Teleport or ObjectType.TeleportBroken or ObjectType.DungeonEntrance
            or ObjectType.TeleportWild or ObjectType.TokenMultiuse or ObjectType.CastleTeleport
            or ObjectType.TournamentTeleport or ObjectType.TokenIsland or ObjectType.TokenIslandGuest => true,
        _ => false
    };

    internal static bool TryGetMapPosition(PacketAnalyzeData pad, out double x, out double z)
    {
        x = z = 0;
        switch (pad)
        {
            case MobPacket m:
                if (m.ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
                {
                    return false;
                }

                x = m.X;
                z = m.Z;
                return true;
            case WorldObject w:
                if (w.ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
                {
                    return false;
                }

                x = w.X;
                z = w.Z;
                return true;
            case NpcTradePacket n:
                if (n.ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
                {
                    return false;
                }

                x = n.X;
                z = n.Z;
                return true;
            case CharacterPacket c:
                if (c.ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
                {
                    return false;
                }

                x = c.X;
                z = c.Z;
                return true;
            case ItemPacket i:
                if (i.ActionType is not (EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2))
                {
                    return false;
                }

                x = i.X;
                z = i.Z;
                return true;
            case CastleChest cc:
                if (cc.ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
                {
                    return false;
                }

                x = cc.X;
                z = cc.Z;
                return true;
            case CastleGate cg:
                if (cg.ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
                {
                    return false;
                }

                x = cg.X;
                z = cg.Z;
                return true;
            case CastleTablet ct:
                if (ct.ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
                {
                    return false;
                }

                x = ct.X;
                z = ct.Z;
                return true;
            default:
                return false;
        }
    }
}
