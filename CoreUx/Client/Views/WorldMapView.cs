using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HawkNet.CoreUx.Client.World;

namespace HawkNet.CoreUx.Client.Views;

public sealed class WorldMapView : Control
{
    public static readonly StyledProperty<WorldViewModel?> WorldProperty =
        AvaloniaProperty.Register<WorldMapView, WorldViewModel?>(nameof(World));

    private WorldViewModel? _subscribedWorld;
    private double _scale = 1d;
    private Vector _offset;
    private double _zoom = 1d;
    private Vector _pan;
    private bool _isPanning;
    private Point _lastPanPoint;

    public WorldMapView()
    {
        ClipToBounds = true;
        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        PointerExited += OnPointerExited;
    }

    public void ZoomIn()
    {
        ZoomAt(new Point(Bounds.Width / 2d, Bounds.Height / 2d), 1.2d);
    }

    public void ZoomOut()
    {
        ZoomAt(new Point(Bounds.Width / 2d, Bounds.Height / 2d), 1d / 1.2d);
    }

    public void ResetView()
    {
        _zoom = 1d;
        _pan = default;
        InvalidateVisual();
    }

    public WorldViewModel? World
    {
        get => GetValue(WorldProperty);
        set => SetValue(WorldProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WorldProperty)
        {
            SubscribeWorld(change.GetOldValue<WorldViewModel?>(), change.GetNewValue<WorldViewModel?>());
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        WorldViewModel? world = World;
        if (world is null || world.Tiles.Count == 0)
        {
            DrawEmptyState(context);
            return;
        }

        UpdateCamera(world);
        DrawTiles(context, world);
        DrawMovementPaths(context, world);
        DrawMovementDebugMarkers(context, world);
        DrawEntities(context, world);
    }

    private void SubscribeWorld(WorldViewModel? oldWorld, WorldViewModel? newWorld)
    {
        if (oldWorld is not null)
        {
            oldWorld.PropertyChanged -= OnWorldChanged;
            oldWorld.Tiles.CollectionChanged -= OnWorldCollectionChanged;
            oldWorld.Entities.CollectionChanged -= OnWorldCollectionChanged;
			oldWorld.EntityFocusRequested -= OnEntityFocusRequested;
            foreach (WorldTileViewModel tile in oldWorld.Tiles)
            {
                tile.PropertyChanged -= OnWorldChanged;
            }

            foreach (WorldEntityViewModel entity in oldWorld.Entities)
            {
                entity.PropertyChanged -= OnWorldChanged;
            }
        }

        _subscribedWorld = newWorld;
        if (newWorld is not null)
        {
            newWorld.PropertyChanged += OnWorldChanged;
            newWorld.Tiles.CollectionChanged += OnWorldCollectionChanged;
            newWorld.Entities.CollectionChanged += OnWorldCollectionChanged;
			newWorld.EntityFocusRequested += OnEntityFocusRequested;
            foreach (WorldTileViewModel tile in newWorld.Tiles)
            {
                tile.PropertyChanged += OnWorldChanged;
            }

            foreach (WorldEntityViewModel entity in newWorld.Entities)
            {
                entity.PropertyChanged += OnWorldChanged;
            }
        }
    }

	private void OnEntityFocusRequested(object? sender, WorldEntityViewModel entity)
	{
		if (World is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
		UpdateCamera(World);
		Point position = WorldToScreen(entity.RenderWorldX, entity.RenderWorldZ);
		_pan += new Vector((Bounds.Width / 2d) - position.X, (Bounds.Height / 2d) - position.Y);
		InvalidateVisual();
	}

    private void OnWorldCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (object item in e.OldItems)
            {
                if (item is INotifyPropertyChanged changed)
                {
                    changed.PropertyChanged -= OnWorldChanged;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (object item in e.NewItems)
            {
                if (item is INotifyPropertyChanged changed)
                {
                    changed.PropertyChanged += OnWorldChanged;
                }
            }
        }

        InvalidateVisual();
    }

    private void OnWorldChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void DrawEmptyState(DrawingContext context)
    {
        context.FillRectangle(PanelBrush, Bounds);
        DrawText(context, "Waiting for world state", new Point(16, 16), MutedTextBrush, 14, FontWeight.Normal);
    }

    private void DrawTiles(DrawingContext context, WorldViewModel world)
    {
        foreach (WorldTileViewModel tile in world.Tiles)
        {
            IBrush fill = tile.IsBlocked
                ? BlockedBrush
                : tile.IsSpawner
                    ? SpawnerBrush
                    : TileBrush;
            if (tile.IsSelected)
            {
                fill = tile.IsBlocked ? SelectedBlockedTileBrush : SelectedTileBrush;
            }
            else if (tile.IsHovered)
            {
                fill = tile.IsBlocked ? HoveredBlockedTileBrush : HoveredTileBrush;
            }

            Pen border = tile.IsBlocked ? BlockedBorderPen : TileBorderPen;
            if (tile.IsHovered || tile.IsSelected)
            {
                border = HighlightPen;
            }

            if (tile.Shape == WorldTileShape.Hex)
            {
                context.DrawGeometry(fill, border, CreateHexGeometry(tile));
            }
            else
            {
                Rect rect = new(WorldToScreen(tile.WorldX, tile.WorldZ), WorldSizeToScreen(tile.SizeX, tile.SizeZ));
                context.DrawRectangle(fill, border, rect, 2);
            }
        }
    }

    private void DrawEntities(DrawingContext context, WorldViewModel world)
    {
        foreach (WorldEntityViewModel entity in world.Entities)
        {
            Point center = WorldToScreen(entity.RenderWorldX, entity.RenderWorldZ);
            double radius = entity.IsLocalPlayer ? 9 : 7;
            IBrush fill = entity.IsLocalPlayer ? LocalPlayerBrush : RemotePlayerBrush;
            Pen pen = entity.IsSelected || entity.IsHovered ? EntityHighlightPen : EntityPen;
            context.DrawEllipse(fill, pen, center, radius, radius);

            string label = entity.DisplayName;
            if (!string.IsNullOrWhiteSpace(label))
            {
                DrawText(context, label, new Point(center.X + 10, center.Y - 20), TextBrush, 12, FontWeight.SemiBold);
            }
        }
    }

    private void DrawMovementPaths(DrawingContext context, WorldViewModel world)
    {
        foreach (WorldMovementPathOverlay overlay in world.VisibleMovementPathOverlays)
        {
            if (overlay.Points.Count < 2)
            {
                continue;
            }

            IBrush brush = GetMovementPathBrush(overlay.Entity);
            var pathPen = new Pen(brush, 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            for (int index = 1; index < overlay.Points.Count; index++)
            {
                WorldPoint previous = overlay.Points[index - 1];
                WorldPoint current = overlay.Points[index];
                context.DrawLine(pathPen, WorldToScreen(previous.X, previous.Z), WorldToScreen(current.X, current.Z));
            }

            Point target = WorldToScreen(overlay.CurrentTarget.X, overlay.CurrentTarget.Z);
            context.DrawEllipse(PanelBrush, new Pen(brush, 2), target, 5, 5);

            Point destination = WorldToScreen(overlay.Destination.X, overlay.Destination.Z);
            context.DrawGeometry(brush, DestinationMarkerBorderPen, CreateDiamondGeometry(destination, 7));
        }
    }

    private void DrawMovementDebugMarkers(DrawingContext context, WorldViewModel world)
    {
        foreach (WorldEntityViewModel entity in world.VisibleMovementDebugEntities)
        {
            IBrush brush = GetMovementPathBrush(entity);
            if (world.ShowServerPositionMarkers)
            {
                DrawCross(context, WorldToScreen(entity.WorldX, entity.WorldZ), ServerPositionMarkerPen, 8);
            }

            if (world.ShowRenderPositionMarkers)
            {
                Point render = WorldToScreen(entity.RenderWorldX, entity.RenderWorldZ);
                context.DrawEllipse(null, new Pen(brush, 2), render, 12, 12);
            }

            if (world.ShowCorrectionLabels)
            {
                Point labelOrigin = WorldToScreen(entity.RenderWorldX, entity.RenderWorldZ);
                DrawText(
                    context,
                    FormatCorrectionMode(entity.LastCorrectionMode),
                    new Point(labelOrigin.X + 12, labelOrigin.Y + 8),
                    CorrectionLabelBrush,
                    11,
                    FontWeight.SemiBold);
            }
        }
    }

    private Geometry CreateHexGeometry(WorldTileViewModel tile)
    {
        Point center = WorldToScreen(tile.CenterX, tile.CenterZ);
        Size size = WorldSizeToScreen(tile.SizeX, tile.SizeZ);
        double rx = size.Width / 2d;
        double rz = size.Height / 2d;
        double startAngle = tile.HexOrientation == WorldHexOrientation.PointyTop ? -90d : 0d;
        var geometry = new StreamGeometry();
        using StreamGeometryContext ctx = geometry.Open();
        for (int index = 0; index < 6; index++)
        {
            double angle = (Math.PI / 180d) * (startAngle + (index * 60d));
            var point = new Point(
                center.X + (Math.Cos(angle) * rx),
                center.Y + (Math.Sin(angle) * rz));
            if (index == 0)
            {
                ctx.BeginFigure(point, isFilled: true);
            }
            else
            {
                ctx.LineTo(point);
            }
        }

        ctx.EndFigure(isClosed: true);
        return geometry;
    }

    private static Geometry CreateDiamondGeometry(Point center, double radius)
    {
        var geometry = new StreamGeometry();
        using StreamGeometryContext ctx = geometry.Open();
        ctx.BeginFigure(new Point(center.X, center.Y - radius), isFilled: true);
        ctx.LineTo(new Point(center.X + radius, center.Y));
        ctx.LineTo(new Point(center.X, center.Y + radius));
        ctx.LineTo(new Point(center.X - radius, center.Y));
        ctx.EndFigure(isClosed: true);
        return geometry;
    }

    private static void DrawCross(DrawingContext context, Point center, Pen pen, double radius)
    {
        context.DrawLine(pen, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
        context.DrawLine(pen, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        WorldViewModel? world = World;
        if (world is null)
        {
            return;
        }

        Point pointer = e.GetPosition(this);
        if (_isPanning)
        {
            _pan += pointer - _lastPanPoint;
            _lastPanPoint = pointer;
            InvalidateVisual();
            return;
        }
        WorldEntityViewModel? entity = HitTestEntity(world, pointer);
        world.HoverEntity(entity);
        world.HoverTile(entity is null ? HitTestTile(world, pointer) : null);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        WorldViewModel? world = World;
        if (world is null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed)
        {
            _isPanning = true;
            _lastPanPoint = point.Position;
            e.Pointer.Capture(this);
            return;
        }
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            world.ClearSelection();
            return;
        }

        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        WorldEntityViewModel? entity = HitTestEntity(world, point.Position);
        if (entity is not null)
        {
            world.SelectEntity(entity);
            return;
        }

        WorldTileViewModel? tile = HitTestTile(world, point.Position);
        if (tile is not null)
        {
            world.ClickTile(tile);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        e.Pointer.Capture(null);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.12d : 1d / 1.12d);
        e.Handled = true;
    }

    private void ZoomAt(Point pointer, double factor)
    {
        double nextZoom = Math.Clamp(_zoom * factor, 0.65d, 5d);
        double appliedFactor = nextZoom / _zoom;
        if (Math.Abs(appliedFactor - 1d) < 0.0001d)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        _pan = new Vector(
            ((1d - appliedFactor) * (pointer.X - center.X)) + (_pan.X * appliedFactor),
            ((1d - appliedFactor) * (pointer.Y - center.Y)) + (_pan.Y * appliedFactor));
        _zoom = nextZoom;
        InvalidateVisual();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        World?.HoverEntity(null);
        World?.HoverTile(null);
    }

    private WorldEntityViewModel? HitTestEntity(WorldViewModel world, Point screenPoint)
    {
        foreach (WorldEntityViewModel entity in world.Entities.Reverse())
        {
            Point center = WorldToScreen(entity.RenderWorldX, entity.RenderWorldZ);
            double radius = entity.IsLocalPlayer ? 11 : 9;
            if (Math.Abs(center.X - screenPoint.X) <= radius && Math.Abs(center.Y - screenPoint.Y) <= radius)
            {
                return entity;
            }
        }

        return null;
    }

    private WorldTileViewModel? HitTestTile(WorldViewModel world, Point screenPoint)
    {
        WorldPoint point = ScreenToWorld(screenPoint);
        foreach (WorldTileViewModel tile in world.Tiles.Reverse())
        {
            if (ContainsWorldPoint(tile, point))
            {
                return tile;
            }
        }

        return null;
    }

    private static bool ContainsWorldPoint(WorldTileViewModel tile, WorldPoint point)
    {
        if (point.X < tile.WorldX
            || point.X > tile.WorldX + tile.SizeX
            || point.Z < tile.WorldZ
            || point.Z > tile.WorldZ + tile.SizeZ)
        {
            return false;
        }

        if (tile.Shape == WorldTileShape.Rectangle)
        {
            return true;
        }

        float normalizedX = Math.Abs(point.X - tile.CenterX) / Math.Max(0.0001f, tile.SizeX / 2f);
        float normalizedZ = Math.Abs(point.Z - tile.CenterZ) / Math.Max(0.0001f, tile.SizeZ / 2f);
        return tile.HexOrientation switch
        {
            WorldHexOrientation.PointyTop => normalizedX <= 1f && normalizedZ <= 1f - (normalizedX * 0.5f),
            WorldHexOrientation.FlatTop => normalizedZ <= 1f && normalizedX <= 1f - (normalizedZ * 0.5f),
            _ => false
        };
    }

    private void UpdateCamera(WorldViewModel world)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || world.Tiles.Count == 0)
        {
            return;
        }

        double minX = world.Tiles.Min(tile => tile.WorldX);
        double minZ = world.Tiles.Min(tile => tile.WorldZ);
        double maxX = world.Tiles.Max(tile => tile.WorldX + tile.SizeX);
        double maxZ = world.Tiles.Max(tile => tile.WorldZ + tile.SizeZ);
        double worldWidth = Math.Max(1d, maxX - minX);
        double worldHeight = Math.Max(1d, maxZ - minZ);
        const double padding = 28d;
        double availableWidth = Math.Max(1d, Bounds.Width - (padding * 2d));
        double availableHeight = Math.Max(1d, Bounds.Height - (padding * 2d));
        double fitScale = Math.Max(8d, Math.Min(availableWidth / worldWidth, availableHeight / worldHeight));
        double drawnWidth = worldWidth * fitScale;
        double drawnHeight = worldHeight * fitScale;
        var fitOffset = new Vector(
            ((Bounds.Width - drawnWidth) / 2d) - (minX * fitScale),
            ((Bounds.Height - drawnHeight) / 2d) - (minZ * fitScale));
        var center = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        _scale = fitScale * _zoom;
        _offset = new Vector(
            center.X + ((fitOffset.X - center.X) * _zoom) + _pan.X,
            center.Y + ((fitOffset.Y - center.Y) * _zoom) + _pan.Y);
    }

    private Point WorldToScreen(float worldX, float worldZ)
    {
        return new Point((worldX * _scale) + _offset.X, (worldZ * _scale) + _offset.Y);
    }

    private Size WorldSizeToScreen(float sizeX, float sizeZ)
    {
        return new Size(sizeX * _scale, sizeZ * _scale);
    }

    private WorldPoint ScreenToWorld(Point screenPoint)
    {
        return new WorldPoint(
            (float)((screenPoint.X - _offset.X) / _scale),
            (float)((screenPoint.Y - _offset.Y) / _scale));
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        Point origin,
        IBrush brush,
        double size,
        FontWeight weight)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, weight),
            size,
            brush);
        context.DrawText(formatted, origin);
    }

    private static IBrush GetMovementPathBrush(WorldEntityViewModel entity)
    {
        if (entity.IsLocalPlayer)
        {
            return LocalPlayerPathBrush;
        }

        int index = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(entity.Id)) % RemotePathBrushes.Length;
        return RemotePathBrushes[index];
    }

    private static string FormatCorrectionMode(WorldCorrectionMode mode)
    {
        return mode switch
        {
            WorldCorrectionMode.Ignore => "ignore",
            WorldCorrectionMode.Blend => "blend",
            WorldCorrectionMode.Snap => "snap",
            _ => "none"
        };
    }

    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.FromRgb(18, 24, 30));
    private static readonly IBrush TileBrush = new SolidColorBrush(Color.FromRgb(44, 65, 62));
    private static readonly IBrush BlockedBrush = new SolidColorBrush(Color.FromRgb(55, 58, 63));
    private static readonly IBrush SpawnerBrush = new SolidColorBrush(Color.FromRgb(54, 88, 72));
    private static readonly IBrush HoveredTileBrush = new SolidColorBrush(Color.FromRgb(73, 101, 100));
    private static readonly IBrush HoveredBlockedTileBrush = new SolidColorBrush(Color.FromRgb(105, 69, 67));
    private static readonly IBrush SelectedTileBrush = new SolidColorBrush(Color.FromRgb(86, 119, 104));
    private static readonly IBrush SelectedBlockedTileBrush = new SolidColorBrush(Color.FromRgb(126, 75, 72));
    private static readonly IBrush LocalPlayerBrush = new SolidColorBrush(Color.FromRgb(238, 196, 96));
    private static readonly IBrush RemotePlayerBrush = new SolidColorBrush(Color.FromRgb(112, 176, 214));
    private static readonly IBrush LocalPlayerPathBrush = new SolidColorBrush(Color.FromRgb(255, 209, 102));
    private static readonly IBrush[] RemotePathBrushes =
    [
        new SolidColorBrush(Color.FromRgb(104, 190, 255)),
        new SolidColorBrush(Color.FromRgb(136, 220, 151)),
        new SolidColorBrush(Color.FromRgb(255, 143, 114)),
        new SolidColorBrush(Color.FromRgb(200, 171, 255)),
        new SolidColorBrush(Color.FromRgb(99, 219, 204))
    ];
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(236, 241, 244));
    private static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.FromRgb(146, 158, 166));
    private static readonly IBrush CorrectionLabelBrush = new SolidColorBrush(Color.FromRgb(245, 236, 189));
    private static readonly Pen TileBorderPen = new(new SolidColorBrush(Color.FromRgb(89, 101, 101)), 1);
    private static readonly Pen BlockedBorderPen = new(new SolidColorBrush(Color.FromRgb(83, 86, 91)), 1);
    private static readonly Pen HighlightPen = new(new SolidColorBrush(Color.FromRgb(236, 203, 122)), 2);
    private static readonly Pen EntityPen = new(new SolidColorBrush(Color.FromRgb(20, 25, 30)), 2);
    private static readonly Pen EntityHighlightPen = new(new SolidColorBrush(Color.FromRgb(255, 247, 198)), 3);
    private static readonly Pen DestinationMarkerBorderPen = new(new SolidColorBrush(Color.FromRgb(20, 25, 30)), 1);
    private static readonly Pen ServerPositionMarkerPen = new(new SolidColorBrush(Color.FromRgb(255, 103, 103)), 2);
}
