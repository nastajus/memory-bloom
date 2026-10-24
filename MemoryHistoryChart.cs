using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MemoryBloom;

internal sealed class MemoryHistoryChart : FrameworkElement
{
    private static readonly TimeSpan MinimumRange = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumRange = TimeSpan.FromDays(30);
    private IReadOnlyList<MemoryHistoryPoint> _points = Array.Empty<MemoryHistoryPoint>();
    private IReadOnlyList<SystemHistoryMarker> _markers = Array.Empty<SystemHistoryMarker>();
    private TimeSpan? _range = TimeSpan.FromHours(6);
    private DateTime _viewEndUtc = DateTime.UtcNow;
    private bool _followLive = true;
    private Point? _hoverPoint;
    private Point? _dragStart;
    private DateTime _dragStartEndUtc;

    public event EventHandler? ViewChanged;
    public DateTime VisibleEndUtc => _followLive ? DateTime.UtcNow : _viewEndUtc;
    public DateTime VisibleStartUtc
    {
        get
        {
            var first = _points.FirstOrDefault()?.TimestampUtc ?? DateTime.UtcNow;
            return _range is null ? first : VisibleEndUtc - _range.Value;
        }
    }
    public TimeSpan? VisibleRange => _range;
    public bool IsFollowingLive => _followLive;

    public MemoryHistoryChart()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => { _hoverPoint = null; InvalidateVisual(); };
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    public void SetData(IReadOnlyList<MemoryHistoryPoint> points, IReadOnlyList<SystemHistoryMarker> markers)
    {
        _points = points;
        _markers = markers;
        if (_followLive) _viewEndUtc = DateTime.UtcNow;
        InvalidateVisual();
    }

    public void SetRange(TimeSpan? range)
    {
        _range = range;
        _followLive = true;
        _viewEndUtc = DateTime.UtcNow;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void FollowLive()
    {
        _followLive = true;
        _viewEndUtc = DateTime.UtcNow;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SetPreviewHover(double horizontalFraction, double verticalFraction)
    {
        _hoverPoint = new Point(
            48 + Math.Clamp(horizontalFraction, 0, 1) * Math.Max(1, ActualWidth - 64),
            22 + Math.Clamp(verticalFraction, 0, 1) * Math.Max(1, ActualHeight - 54));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(25, 34, 37)), null, bounds, 12, 12);
        if (ActualWidth < 180 || ActualHeight < 120) return;

        var plot = new Rect(48, 22, Math.Max(1, ActualWidth - 64), Math.Max(1, ActualHeight - 54));
        var start = VisibleStartUtc;
        var end = VisibleEndUtc;
        if (end <= start) end = start + TimeSpan.FromMinutes(1);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var muted = new SolidColorBrush(Color.FromRgb(147, 164, 169));
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(68, 111, 130, 136)), 1);

        drawingContext.PushClip(new RectangleGeometry(plot));
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 255, 104, 92)), null,
            new Rect(plot.Left, YFor(100, plot), plot.Width, YFor(90, plot) - YFor(100, plot)));
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(20, 244, 190, 82)), null,
            new Rect(plot.Left, YFor(90, plot), plot.Width, YFor(78, plot) - YFor(90, plot)));
        drawingContext.Pop();

        foreach (var value in new[] { 0, 25, 50, 75, 100 })
        {
            var y = YFor(value, plot);
            drawingContext.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(drawingContext, $"{value}%", new Point(7, y - 8), 10, muted, dpi);
        }

        for (var index = 0; index <= 4; index++)
        {
            var fraction = index / 4d;
            var x = plot.Left + plot.Width * fraction;
            var timestamp = start + TimeSpan.FromTicks((long)((end - start).Ticks * fraction));
            drawingContext.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            DrawText(drawingContext, FormatAxisTime(timestamp, end - start), new Point(x - 29, plot.Bottom + 7), 9, muted, dpi);
        }

        var visible = _points.Where(point => point.TimestampUtc >= start && point.TimestampUtc <= end).ToList();
        DrawSeries(drawingContext, visible, point => point.PhysicalPercent, new Pen(new SolidColorBrush(Color.FromRgb(103, 222, 181)), 2.2), plot, start, end);
        DrawSeries(drawingContext, visible, point => point.CommitPercent, new Pen(new SolidColorBrush(Color.FromRgb(189, 142, 255)), 1.8), plot, start, end);

        foreach (var point in visible.Where(point => point.Classification != "Normal"))
        {
            var x = XFor(point.TimestampUtc, plot, start, end);
            var color = point.Classification == "Critical" ? Color.FromRgb(255, 104, 92) :
                point.Classification == "Recovered" ? Color.FromRgb(103, 222, 181) : Color.FromRgb(244, 190, 82);
            drawingContext.DrawEllipse(new SolidColorBrush(color), new Pen(Brushes.White, 1), new Point(x, YFor(point.PressurePercent, plot)), 4, 4);
        }

        foreach (var marker in _markers.Where(marker => marker.TimestampUtc >= start && marker.TimestampUtc <= end))
        {
            var x = XFor(marker.TimestampUtc, plot, start, end);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(190, 255, 125, 112)), 1) { DashStyle = DashStyles.Dash };
            drawingContext.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var triangle = new StreamGeometry();
            using (var context = triangle.Open())
            {
                context.BeginFigure(new Point(x, plot.Top + 2), true, true);
                context.LineTo(new Point(x - 5, plot.Top + 11), true, false);
                context.LineTo(new Point(x + 5, plot.Top + 11), true, false);
            }
            drawingContext.DrawGeometry(new SolidColorBrush(Color.FromRgb(255, 125, 112)), null, triangle);
            if (marker.PressurePercent is double pressure)
                drawingContext.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 125, 112)), new Pen(Brushes.White, 1), new Point(x, YFor(pressure, plot)), 5, 5);
        }

        DrawLegend(drawingContext, plot, dpi);
        if (_hoverPoint is Point hover) DrawHover(drawingContext, hover, visible, plot, start, end, dpi);
    }

    private void DrawHover(DrawingContext context, Point hover, List<MemoryHistoryPoint> visible, Rect plot,
        DateTime start, DateTime end, double dpi)
    {
        if (!plot.Contains(hover) || visible.Count == 0) return;
        var timestamp = start + TimeSpan.FromTicks((long)((end - start).Ticks * Math.Clamp((hover.X - plot.Left) / plot.Width, 0, 1)));
        var nearest = visible.MinBy(point => Math.Abs((point.TimestampUtc - timestamp).Ticks));
        if (nearest is null) return;
        var nearestMarker = _markers
            .Where(marker => marker.TimestampUtc >= start && marker.TimestampUtc <= end)
            .MinBy(marker => Math.Abs((marker.TimestampUtc - timestamp).Ticks));
        var markerDistance = nearestMarker is null ? double.MaxValue : Math.Abs(XFor(nearestMarker.TimestampUtc, plot, start, end) - hover.X);
        var pointDistance = Math.Abs(XFor(nearest.TimestampUtc, plot, start, end) - hover.X);
        if (nearestMarker is not null && markerDistance <= Math.Min(10, pointDistance))
        {
            DrawMarkerHover(context, nearestMarker, hover, plot, start, end, dpi);
            return;
        }
        var x = XFor(nearest.TimestampUtc, plot, start, end);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(180, 230, 238, 240)), 1), new Point(x, plot.Top), new Point(x, plot.Bottom));

        var processText = nearest.TopProcesses.Count == 0
            ? "Process context not yet sampled"
            : string.Join("\n", nearest.TopProcesses.Take(3).Select(process => $"{process.Name}  {FormatBytes(process.PrivateBytes)}"));
        var state = nearest.Classification != "Normal" ? $"{nearest.Classification} event" :
            nearest.PressurePercent >= 90 ? "Critical pressure" : nearest.PressurePercent >= 78 ? "High pressure" : "Normal range";
        var label = $"{nearest.TimestampUtc.ToLocalTime():g}\nPhysical {nearest.PhysicalPercent:0.0}%  ·  Commit {nearest.CommitPercent:0.0}%\n{state}\n{processText}";
        var formatted = MakeText(label, 11, Brushes.White, dpi);
        var width = Math.Min(310, formatted.Width + 20);
        var height = formatted.Height + 16;
        var left = Math.Clamp(x + 10, plot.Left + 4, plot.Right - width - 4);
        var top = Math.Clamp(hover.Y - height - 12, plot.Top + 4, plot.Bottom - height - 4);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(245, 15, 21, 23)), new Pen(new SolidColorBrush(Color.FromRgb(83, 101, 106)), 1), new Rect(left, top, width, height), 8, 8);
        context.DrawText(formatted, new Point(left + 10, top + 8));
    }

    private static void DrawMarkerHover(DrawingContext context, SystemHistoryMarker marker, Point hover, Rect plot,
        DateTime start, DateTime end, double dpi)
    {
        var x = XFor(marker.TimestampUtc, plot, start, end);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(255, 125, 112)), 1.5), new Point(x, plot.Top), new Point(x, plot.Bottom));
        var label = $"{marker.TimestampUtc.ToLocalTime():g}\n{marker.Kind}\n{marker.Summary}\n{marker.Details}";
        var formatted = MakeText(label, 11, Brushes.White, dpi);
        var width = Math.Min(330, formatted.Width + 20);
        var height = formatted.Height + 16;
        var left = Math.Clamp(x + 10, plot.Left + 4, plot.Right - width - 4);
        var top = Math.Clamp(hover.Y - height - 12, plot.Top + 4, plot.Bottom - height - 4);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(245, 15, 21, 23)), new Pen(new SolidColorBrush(Color.FromRgb(255, 125, 112)), 1), new Rect(left, top, width, height), 8, 8);
        context.DrawText(formatted, new Point(left + 10, top + 8));
    }

    private static void DrawLegend(DrawingContext context, Rect plot, double dpi)
    {
        var y = 3d;
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(103, 222, 181)), 2.2), new Point(plot.Left, y + 7), new Point(plot.Left + 18, y + 7));
        DrawText(context, "Physical", new Point(plot.Left + 24, y), 10, Brushes.White, dpi);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(189, 142, 255)), 1.8), new Point(plot.Left + 93, y + 7), new Point(plot.Left + 111, y + 7));
        DrawText(context, "Commit", new Point(plot.Left + 117, y), 10, Brushes.White, dpi);
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(244, 190, 82)), null, new Point(plot.Left + 186, y + 7), 4, 4);
        DrawText(context, "Spike", new Point(plot.Left + 196, y), 10, Brushes.White, dpi);
        var eventPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 125, 112)), 1) { DashStyle = DashStyles.Dash };
        context.DrawLine(eventPen, new Point(plot.Left + 242, y + 1), new Point(plot.Left + 242, y + 14));
        DrawText(context, "Windows event", new Point(plot.Left + 250, y), 10, Brushes.White, dpi);
    }

    private static void DrawSeries(DrawingContext context, List<MemoryHistoryPoint> points,
        Func<MemoryHistoryPoint, double> selector, Pen pen, Rect plot, DateTime start, DateTime end)
    {
        if (points.Count < 2) return;
        var geometry = new StreamGeometry();
        var maximumGap = (end - start) <= TimeSpan.FromDays(1) ? TimeSpan.FromMinutes(2) :
            (end - start) <= TimeSpan.FromDays(7) ? TimeSpan.FromMinutes(12) : TimeSpan.FromMinutes(35);
        using (var path = geometry.Open())
        {
            var first = points[0];
            path.BeginFigure(new Point(XFor(first.TimestampUtc, plot, start, end), YFor(selector(first), plot)), false, false);
            var previous = first;
            foreach (var point in points.Skip(1))
            {
                var coordinate = new Point(XFor(point.TimestampUtc, plot, start, end), YFor(selector(point), plot));
                if (point.TimestampUtc - previous.TimestampUtc > maximumGap)
                    path.BeginFigure(coordinate, false, false);
                else
                    path.LineTo(coordinate, true, false);
                previous = point;
            }
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var current = _range ?? MaximumRange;
        var factor = e.Delta > 0 ? 0.67 : 1.5;
        var next = TimeSpan.FromTicks((long)Math.Clamp(current.Ticks * factor, MinimumRange.Ticks, MaximumRange.Ticks));
        var plot = new Rect(48, 22, Math.Max(1, ActualWidth - 64), Math.Max(1, ActualHeight - 54));
        var position = e.GetPosition(this);
        var fraction = Math.Clamp((position.X - plot.Left) / plot.Width, 0, 1);
        var oldEnd = VisibleEndUtc;
        var anchor = oldEnd - TimeSpan.FromTicks((long)(current.Ticks * (1 - fraction)));
        _range = next;
        _viewEndUtc = anchor + TimeSpan.FromTicks((long)(next.Ticks * (1 - fraction)));
        _followLive = Math.Abs((_viewEndUtc - DateTime.UtcNow).TotalSeconds) < 15;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            FollowLive();
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
        _dragStartEndUtc = VisibleEndUtc;
        CaptureMouse();
        Cursor = Cursors.SizeWE;
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_dragStart is Point start && e.LeftButton == MouseButtonState.Pressed)
        {
            var range = _range ?? MaximumRange;
            var secondsPerPixel = range.TotalSeconds / Math.Max(1, ActualWidth - 64);
            _viewEndUtc = _dragStartEndUtc + TimeSpan.FromSeconds((start.X - position.X) * secondsPerPixel);
            var latest = _points.LastOrDefault()?.TimestampUtc ?? DateTime.UtcNow;
            if (_viewEndUtc > latest) _viewEndUtc = latest;
            _followLive = false;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _hoverPoint = position;
        }
        InvalidateVisual();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        ReleaseMouseCapture();
        Cursor = Cursors.Cross;
        e.Handled = true;
    }

    private static double XFor(DateTime timestamp, Rect plot, DateTime start, DateTime end) =>
        plot.Left + Math.Clamp((timestamp - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds), 0, 1) * plot.Width;
    private static double YFor(double value, Rect plot) => plot.Bottom - Math.Clamp(value, 0, 100) / 100d * plot.Height;

    private static string FormatAxisTime(DateTime utc, TimeSpan span) =>
        span <= TimeSpan.FromHours(24) ? utc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) : utc.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);

    private static void DrawText(DrawingContext context, string text, Point point, double size, Brush brush, double dpi) =>
        context.DrawText(MakeText(text, size, brush, dpi), point);

    private static FormattedText MakeText(string text, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi)
        { MaxTextWidth = 300 };

    private static string FormatBytes(long bytes)
    {
        var value = Math.Max(0, bytes);
        if (value >= 1024L * 1024 * 1024) return $"{value / (1024d * 1024 * 1024):0.0} GB";
        return $"{value / (1024d * 1024):0} MB";
    }
}
