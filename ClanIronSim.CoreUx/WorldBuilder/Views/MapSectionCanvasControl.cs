using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ClanIronSim.World;
using ClanIronSim.CoreUx.WorldBuilder.ViewModels;

namespace ClanIronSim.CoreUx.WorldBuilder.Views;

public sealed class MapSectionCanvasControl : Control
{
    private readonly Dictionary<string, Bitmap> _bitmaps = new();
    private Point _pan;
    private Point? _panStart;
    private bool _isDrawing;
    private (int X, int Z) _lastDrawn = (-1, -1);

    public MapSectionCanvasControl()
    {
        ClipToBounds = true;
        foreach (string uri in new[] { "avares://ClanIronSim.CoreUx/Assets/forest-floor-tile.png", "avares://ClanIronSim.CoreUx/Assets/rough-ground-tile.png" })
            using (Stream stream = AssetLoader.Open(new Uri(uri))) _bitmaps[uri] = new Bitmap(stream);
        DataContextChanged += (_, _) => HookViewModel();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0D1318")), Bounds);
        if (DataContext is not MapSectionEditorViewModel vm) return;
        double cell = 58 * vm.Zoom;
        var line = new Pen(new SolidColorBrush(Color.Parse("#3A4A54")), 1);
        for (int z = 0; z < vm.Rows; z++) for (int x = 0; x < vm.Columns; x++)
        {
            Geometry geometry = CellGeometry(vm, x, z, cell);
            SectionCellViewModel? placed = vm.Cells.FirstOrDefault(c => c.X == x && c.Z == z);
            if (placed is not null && _bitmaps.TryGetValue(placed.Tile.AssetUri, out Bitmap? bitmap))
            {
                Rect rect = geometry.Bounds;
                using (context.PushGeometryClip(geometry)) context.DrawImage(bitmap, new Rect(bitmap.Size), rect);
            }
            context.DrawGeometry(null, line, geometry);
            if (ReferenceEquals(placed, vm.SelectedCell)) context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#38BDF8")), 3), geometry);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is not MapSectionEditorViewModel vm) return;
        Point point = e.GetPosition(this);
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        if (vm.Tool == "Pan" || props.IsMiddleButtonPressed) { _panStart = point; e.Pointer.Capture(this); return; }
        (int x, int z) = HitCell(vm, point); if (x >= 0) { vm.Interact(x, z); _lastDrawn = (x, z); }
        _isDrawing = vm.Tool is "Paint" or "Erase";
        if (_isDrawing) e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point current = e.GetPosition(this);
        if (_panStart is Point start) { _pan = new Point(_pan.X + current.X - start.X, _pan.Y + current.Y - start.Y); _panStart = current; InvalidateVisual(); return; }
        if (_isDrawing && DataContext is MapSectionEditorViewModel vm)
        {
            (int x, int z) = HitCell(vm, current);
            if (x >= 0 && (x, z) != _lastDrawn) { vm.Interact(x, z); _lastDrawn = (x, z); InvalidateVisual(); }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _panStart = null; _isDrawing = false; _lastDrawn = (-1, -1); e.Pointer.Capture(null); base.OnPointerReleased(e); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { if (DataContext is MapSectionEditorViewModel vm) vm.Zoom += e.Delta.Y * .1; base.OnPointerWheelChanged(e); }

    private Geometry CellGeometry(MapSectionEditorViewModel vm, int x, int z, double cell)
    {
        double ox = _pan.X, oy = _pan.Y;
        if (vm.Shape == MapTileShape.Rectangle) return new RectangleGeometry(new Rect(ox + x * cell, oy + z * cell, cell, cell));
        bool pointy = vm.Orientation == HexOrientation.PointyTop;
        double radius = cell * .56;
        double centerX = pointy
            ? ox + Math.Sqrt(3) * radius * (x + z * .5) + radius
            : ox + 1.5 * radius * x + radius;
        double centerY = pointy
            ? oy + 1.5 * radius * z + radius
            : oy + Math.Sqrt(3) * radius * (z + x * .5) + radius;
        double startAngle = pointy ? -90 : 0;
        Point[] p = Enumerable.Range(0, 6).Select(i =>
        {
            double angle = (startAngle + i * 60) * Math.PI / 180d;
            return new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
        }).ToArray();
        var g = new StreamGeometry(); using StreamGeometryContext c = g.Open(); c.BeginFigure(p[0], true); foreach (Point q in p.Skip(1)) c.LineTo(q); c.EndFigure(true); return g;
    }

    private (int X, int Z) HitCell(MapSectionEditorViewModel vm, Point point)
    { for (int z=0; z<vm.Rows; z++) for (int x=0; x<vm.Columns; x++) if (CellGeometry(vm,x,z,58*vm.Zoom).FillContains(point)) return (x,z); return (-1,-1); }

    private void HookViewModel()
    {
        if (DataContext is not MapSectionEditorViewModel vm) return;
        vm.PropertyChanged += (_, _) => InvalidateVisual();
        vm.Cells.CollectionChanged += (_, _) => InvalidateVisual();
        vm.FitRequested += FitGrid;
        AttachedToVisualTree += (_, _) => FitGrid();
    }

    private void FitGrid()
    {
        if (DataContext is not MapSectionEditorViewModel vm || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        const double baseCell = 58d;
        (double rawWidth, double rawHeight) = MeasureGrid(vm, baseCell);
        const double padding = 56d;
        double scale = Math.Min(
            Math.Max(1, Bounds.Width - padding) / Math.Max(1, rawWidth),
            Math.Max(1, Bounds.Height - padding) / Math.Max(1, rawHeight));
        vm.Zoom = Math.Clamp(scale, .2, 2.5);
        _pan = new Point(
            (Bounds.Width - rawWidth * vm.Zoom) / 2d,
            (Bounds.Height - rawHeight * vm.Zoom) / 2d);
        InvalidateVisual();
    }

    private static (double Width, double Height) MeasureGrid(MapSectionEditorViewModel vm, double cell)
    {
        if (vm.Shape == MapTileShape.Rectangle)
        {
            return (vm.Columns * cell, vm.Rows * cell);
        }

        double radius = cell * .56;
        if (vm.Orientation == HexOrientation.PointyTop)
        {
            double centerSpanX = Math.Sqrt(3) * radius * ((vm.Columns - 1) + ((vm.Rows - 1) * .5));
            double centerSpanY = 1.5 * radius * (vm.Rows - 1);
            return (centerSpanX + (2 * radius), centerSpanY + (2 * radius));
        }

        double flatCenterSpanX = 1.5 * radius * (vm.Columns - 1);
        double flatCenterSpanY = Math.Sqrt(3) * radius * ((vm.Rows - 1) + ((vm.Columns - 1) * .5));
        return (flatCenterSpanX + (2 * radius), flatCenterSpanY + (2 * radius));
    }
}
