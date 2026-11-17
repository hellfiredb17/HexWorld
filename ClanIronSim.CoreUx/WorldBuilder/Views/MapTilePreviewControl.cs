using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ClanIronSim.World;

namespace ClanIronSim.CoreUx.WorldBuilder.Views;

public sealed class MapTilePreviewControl : Control
{
    public static readonly StyledProperty<MapTileShape> ShapeProperty = AvaloniaProperty.Register<MapTilePreviewControl, MapTileShape>(nameof(Shape));
    public static readonly StyledProperty<HexOrientation> OrientationProperty = AvaloniaProperty.Register<MapTilePreviewControl, HexOrientation>(nameof(Orientation));
    public static readonly StyledProperty<double> TileWidthProperty = AvaloniaProperty.Register<MapTilePreviewControl, double>(nameof(TileWidth), 1d);
    public static readonly StyledProperty<double> TileHeightProperty = AvaloniaProperty.Register<MapTilePreviewControl, double>(nameof(TileHeight), 1d);
    public static readonly StyledProperty<bool> IsInteractiveProperty = AvaloniaProperty.Register<MapTilePreviewControl, bool>(nameof(IsInteractive));
    private Bitmap? _texture;
    private Point? _dragStart;
    private double _dragStartRotation;
    private double _rotationRadians;
    private double _zoom = 1d;

    static MapTilePreviewControl() => AffectsRender<MapTilePreviewControl>(ShapeProperty, OrientationProperty, TileWidthProperty, TileHeightProperty);

    public MapTilePreviewControl()
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://ClanIronSim.CoreUx/Assets/forest-floor-tile.png"));
        _texture = new Bitmap(stream);
    }

    public MapTileShape Shape { get => GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public HexOrientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public double TileWidth { get => GetValue(TileWidthProperty); set => SetValue(TileWidthProperty, value); }
    public double TileHeight { get => GetValue(TileHeightProperty); set => SetValue(TileHeightProperty, value); }
    public bool IsInteractive { get => GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }

    /// <summary>
    /// Updates the preview texture from a local image. A null path removes the texture.
    /// </summary>
    public void SetTexturePath(string? texturePath)
    {
        Bitmap? replacement = null;
        if (!string.IsNullOrWhiteSpace(texturePath))
        {
            using FileStream stream = File.OpenRead(texturePath);
            replacement = new Bitmap(stream);
        }

        Bitmap? previous = _texture;
        _texture = replacement;
        previous?.Dispose();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = Bounds.Deflate(1);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0D1318")), bounds);
        const double visibleUnits = 6d;
        double cell = Math.Min(bounds.Width, bounds.Height) / visibleUnits;
        double centerX = bounds.Width / 2d;
        double centerY = bounds.Height / 2d;
        double gridLeft = centerX - (Math.Floor(centerX / cell) * cell);
        double gridTop = centerY - (Math.Floor(centerY / cell) * cell);
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#31414C")), 1);
        for (double x = gridLeft; x <= bounds.Width; x += cell) context.DrawLine(gridPen, new Point(x, 0), new Point(x, bounds.Height));
        for (double y = gridTop; y <= bounds.Height; y += cell) context.DrawLine(gridPen, new Point(0, y), new Point(bounds.Width, y));

        double tileWidth = Math.Clamp(TileWidth * _zoom, .1d, visibleUnits - .2d) * cell;
        double tileHeight = Math.Clamp(TileHeight * _zoom, .1d, visibleUnits - .2d) * cell;
        Rect tileBounds = new(centerX - tileWidth / 2d, centerY - tileHeight / 2d, tileWidth, tileHeight);
        Geometry geometry = CreateTileGeometry(tileBounds);
        using (context.PushTransform(Matrix.CreateRotation(_rotationRadians, new Point(centerX, centerY))))
        {
            using (context.PushGeometryClip(geometry))
            {
                if (_texture is not null)
                {
                    context.DrawImage(_texture, new Rect(_texture.Size), tileBounds);
                }
                else
                {
                    context.FillRectangle(new SolidColorBrush(Color.Parse("#26343E")), tileBounds);
                }
            }

            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#38BDF8")), 3), geometry);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsInteractive || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragStartRotation = _rotationRadians;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!IsInteractive || _dragStart is not Point start)
        {
            return;
        }

        Point current = e.GetPosition(this);
        _rotationRadians = _dragStartRotation + ((current.X - start.X) * .012d);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragStart is not null)
        {
            _dragStart = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        base.OnPointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsInteractive)
        {
            return;
        }

        _zoom = Math.Clamp(_zoom + (e.Delta.Y * .1d), .55d, 1.5d);
        InvalidateVisual();
        e.Handled = true;
    }

    private Geometry CreateTileGeometry(Rect rect)
    {
        if (Shape == MapTileShape.Rectangle) return new RectangleGeometry(rect);
        Point[] points = Orientation == HexOrientation.PointyTop
            ? new[] { new Point(rect.Center.X, rect.Top), new Point(rect.Right, rect.Top + rect.Height * .25), new Point(rect.Right, rect.Bottom - rect.Height * .25), new Point(rect.Center.X, rect.Bottom), new Point(rect.Left, rect.Bottom - rect.Height * .25), new Point(rect.Left, rect.Top + rect.Height * .25) }
            : new[] { new Point(rect.Left + rect.Width * .25, rect.Top), new Point(rect.Right - rect.Width * .25, rect.Top), new Point(rect.Right, rect.Center.Y), new Point(rect.Right - rect.Width * .25, rect.Bottom), new Point(rect.Left + rect.Width * .25, rect.Bottom), new Point(rect.Left, rect.Center.Y) };
        var geometry = new StreamGeometry();
        using StreamGeometryContext drawing = geometry.Open();
        drawing.BeginFigure(points[0], true);
        foreach (Point point in points.Skip(1)) drawing.LineTo(point);
        drawing.EndFigure(true);
        return geometry;
    }
}
