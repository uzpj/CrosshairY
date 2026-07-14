using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CrosshairY;

public partial class CrosshairOverlay : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    private const uint WDA_NONE               = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_LAYERED     = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW  = 0x00000080;
    private const int WS_EX_APPWINDOW   = 0x00040000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private IntPtr _hwnd;
    private WinEventDelegate? _winEventProc;
    private IntPtr _winEventHook;
    private DispatcherTimer? _topmostTimer;

    private const int CursorSize = 256;
    private readonly TranslateTransform _xform = new();

    private bool _dragMode;
    private bool _dragging;

    public event Action<int, int>? ImageDragged;

    public CrosshairOverlay()
    {
        InitializeComponent();
        Width  = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Left   = 0;
        Top    = 0;
        OverlayCanvas.RenderTransform = _xform;

        RenderOptions.SetEdgeMode(OverlayCanvas, EdgeMode.Aliased);
        OverlayCanvas.SnapsToDevicePixels = true;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            SetWindowDisplayAffinity(_hwnd, WDA_NONE);
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            ex |=  WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            ex &= ~WS_EX_APPWINDOW;
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);

            ReassertTopmost();

            _winEventProc = (_, _, _, _, _, _, _) => ReassertTopmost();
            _winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            _topmostTimer.Tick += (_, _) => ReassertTopmost();
            _topmostTimer.Start();
        };

        Closed += (_, _) =>
        {
            CursorReplacer.Restore();
            _topmostTimer?.Stop();
            if (_winEventHook != IntPtr.Zero) UnhookWinEvent(_winEventHook);
        };

        OverlayCanvas.MouseLeftButtonDown += DragMode_MouseDown;
        OverlayCanvas.MouseMove           += DragMode_MouseMove;
        OverlayCanvas.MouseLeftButtonUp   += DragMode_MouseUp;
    }

    private void ReassertTopmost()
    {
        if (!IsVisible || _hwnd == IntPtr.Zero) return;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void Conceal()
    {
        CursorReplacer.Restore();
        if (IsVisible) Hide();
    }

    public void SetProof(bool active)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowDisplayAffinity(hwnd, active ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    public void ApplyMonitor(int index, double scaleX, double scaleY)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (index < 0 || index >= screens.Length) index = 0;
        if (screens.Length == 0) return;

        var b = screens[index].Bounds;
        if (scaleX <= 0) scaleX = 1;
        if (scaleY <= 0) scaleY = 1;

        Left   = b.Left   / scaleX;
        Top    = b.Top    / scaleY;
        Width  = b.Width  / scaleX;
        Height = b.Height / scaleY;
    }

    public void UpdateCrosshair(string template, string color, bool outline, int outlineSize, int size, int opacity, int gap, int offsetX, int offsetY, bool follow)
    {
        if (string.IsNullOrEmpty(template))
        {
            CursorReplacer.Restore();
            if (IsVisible) Hide();
            return;
        }

        if (follow)
        {
            var bmp = RenderCursorBitmap(
                c => CrDraw.Draw(c, CursorSize / 2.0, CursorSize / 2.0, size / 100.0, color, outline, outlineSize, template, gap),
                opacity);
            ApplyCursor(bmp, offsetX, offsetY);
            if (IsVisible) Hide();
            return;
        }

        CursorReplacer.Restore();

        double cx = Width  / 2.0;
        double cy = Height / 2.0;
        OverlayCanvas.Opacity = opacity / 100.0;
        CrDraw.Draw(OverlayCanvas, cx, cy, size / 100.0, color, outline, outlineSize, template, gap);

        _xform.X = offsetX;
        _xform.Y = offsetY;

        if (!IsVisible) Show();
    }

    public void UpdateCustomCrosshair(List<string> pixels, int size, int opacity, int gridSize, int offsetX, int offsetY, bool follow)
    {
        if (pixels.Count == 0 || gridSize <= 0)
        {
            CursorReplacer.Restore();
            OverlayCanvas.Children.Clear();
            if (IsVisible) Hide();
            return;
        }

        if (follow)
        {
            var bmp = RenderCursorBitmap(
                c => DrawCustomInto(c, pixels, gridSize, size / 100.0, CursorSize / 2.0, CursorSize / 2.0),
                opacity);
            ApplyCursor(bmp, offsetX, offsetY);
            OverlayCanvas.Children.Clear();
            if (IsVisible) Hide();
            return;
        }

        CursorReplacer.Restore();

        OverlayCanvas.Children.Clear();
        OverlayCanvas.Opacity = opacity / 100.0;
        DrawCustomInto(OverlayCanvas, pixels, gridSize, size / 100.0, Width / 2.0, Height / 2.0);

        _xform.X = offsetX;
        _xform.Y = offsetY;

        if (!IsVisible) Show();
    }

    public void UpdateImageCrosshair(string fullPath, int size, int opacity, int offsetX, int offsetY, bool follow)
    {
        if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath))
        {
            CursorReplacer.Restore();
            OverlayCanvas.Children.Clear();
            if (IsVisible) Hide();
            return;
        }

        if (follow)
        {
            var bmp = RenderCursorBitmap(c =>
            {
                try
                {
                    var bi = LoadBitmap(fullPath);
                    double scale = size / 100.0;
                    double w = bi.PixelWidth * scale;
                    double h = bi.PixelHeight * scale;
                    var img = new System.Windows.Controls.Image { Source = bi, Width = w, Height = h };
                    Canvas.SetLeft(img, (CursorSize - w) / 2.0);
                    Canvas.SetTop(img, (CursorSize - h) / 2.0);
                    c.Children.Add(img);
                }
                catch { }
            }, opacity);
            ApplyCursor(bmp, offsetX, offsetY);
            OverlayCanvas.Children.Clear();
            if (IsVisible) Hide();
            return;
        }

        CursorReplacer.Restore();

        OverlayCanvas.Children.Clear();
        OverlayCanvas.Opacity = opacity / 100.0;

        try
        {
            var bi = LoadBitmap(fullPath);
            double scale = size / 100.0;
            double w = bi.PixelWidth * scale;
            double h = bi.PixelHeight * scale;

            var img = new System.Windows.Controls.Image
            {
                Source = bi,
                Width = w,
                Height = h,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };

            double cx = Width / 2.0 + offsetX;
            double cy = Height / 2.0 + offsetY;

            Canvas.SetLeft(img, cx - w / 2.0);
            Canvas.SetTop(img, cy - h / 2.0);
            OverlayCanvas.Children.Add(img);
        }
        catch { }

        _xform.X = 0;
        _xform.Y = 0;

        if (!IsVisible) Show();
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    public void SetDragMode(bool on)
    {
        _dragMode = on;
        if (_hwnd != IntPtr.Zero)
        {
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            if (on) ex &= ~WS_EX_TRANSPARENT;
            else    ex |= WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
        }
        OverlayCanvas.Cursor = on ? Cursors.SizeAll : Cursors.None;
    }

    private void DragMode_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (!_dragMode) return;
        _dragging = true;
        OverlayCanvas.CaptureMouse();
        DragMode_MouseMove(s, e);
        e.Handled = true;
    }

    private void DragMode_MouseMove(object s, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        int x = (int)(p.X - Width / 2.0);
        int y = (int)(p.Y - Height / 2.0);
        ImageDragged?.Invoke(x, y);
    }

    private void DragMode_MouseUp(object s, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (OverlayCanvas.IsMouseCaptured) OverlayCanvas.ReleaseMouseCapture();
    }

    private static void DrawCustomInto(Canvas canvas, List<string> pixels, int gridSize, double scale, double cx, double cy)
    {
        double field = 60.0 * scale;
        double cell  = field / gridSize;
        double ox    = cx - field / 2.0;
        double oy    = cy - field / 2.0;

        var byColor = new Dictionary<string, GeometryGroup>();

        foreach (var entry in pixels)
        {
            var parts = entry.Split(',');
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0], out int row) || !int.TryParse(parts[1], out int col)) continue;
            if (row < 0 || row >= gridSize || col < 0 || col >= gridSize) continue;

            var hex = parts[2];
            if (!byColor.TryGetValue(hex, out var group))
            {
                group = new GeometryGroup();
                byColor[hex] = group;
            }

            group.Children.Add(new RectangleGeometry(new Rect(
                Math.Round(ox + col * cell),
                Math.Round(oy + row * cell),
                Math.Ceiling(cell),
                Math.Ceiling(cell))));
        }

        foreach (var (hex, group) in byColor)
        {
            Color color;
            try   { color = (Color)ColorConverter.ConvertFromString(hex); }
            catch { color = Colors.White; }

            group.Freeze();
            var path = new Path { Data = group, Fill = new SolidColorBrush(color), SnapsToDevicePixels = true };
            RenderOptions.SetEdgeMode(path, EdgeMode.Aliased);
            canvas.Children.Add(path);
        }
    }

    private static System.Drawing.Bitmap RenderCursorBitmap(Action<Canvas> draw, int opacity)
    {
        int n = CursorSize;

        var inner = new Canvas { Width = n, Height = n, Opacity = System.Math.Clamp(opacity / 100.0, 0.0, 1.0) };
        draw(inner);

        var host = new Canvas { Width = n, Height = n };
        host.Children.Add(inner);
        host.Measure(new System.Windows.Size(n, n));
        host.Arrange(new Rect(0, 0, n, n));
        host.UpdateLayout();

        var rtb = new RenderTargetBitmap(n, n, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        return new System.Drawing.Bitmap(ms);
    }

    private static void ApplyCursor(System.Drawing.Bitmap bmp, int offsetX, int offsetY)
    {
        int n    = CursorSize;
        int hotX = System.Math.Clamp(n / 2 - offsetX, 0, n - 1);
        int hotY = System.Math.Clamp(n / 2 - offsetY, 0, n - 1);

        CursorReplacer.Apply(bmp, hotX, hotY);
        bmp.Dispose();
    }
}

internal static class CrDraw
{
    public static void Draw(Canvas canvas, double cx, double cy, double scale,
        string colorHex, bool outline, double outSize, string template, int gap = 3)
    {
        canvas.Children.Clear();

        Color col;
        try   { col = (Color)ColorConverter.ConvertFromString(colorHex); }
        catch { col = Colors.White; }

        var brush = new SolidColorBrush(col);
        var black = Brushes.Black;

        switch (template)
        {
            case "dot":
                if (outline) PutEllipseFilled(canvas, cx, cy, (3 + outSize) * scale, black);
                PutEllipseFilled(canvas, cx, cy, 3 * scale, brush);
                break;

            case "ring":
            {
                double r  = 5  * scale;
                double st = 1.5 * scale;
                if (outline) PutEllipseStroked(canvas, cx, cy, r, st + outSize * 2, black);
                PutEllipseStroked(canvas, cx, cy, r, st, brush);
                break;
            }

            case "sq_dot":
                if (outline) PutRect(canvas, cx, cy, (3 + outSize) * scale, (3 + outSize) * scale, black);
                PutRect(canvas, cx, cy, 3 * scale, 3 * scale, brush);
                break;

            case "thin_cross":
                DrawCrossLines(canvas, cx, cy, scale, brush, black, outline, outSize, gap, 8, 1.5, false, false, false);
                break;

            case "thick_cross":
                DrawCrossLines(canvas, cx, cy, scale, brush, black, outline, outSize, gap, 6, 3.0, false, false, false);
                break;

            case "cross_dot_c":
                DrawCrossLines(canvas, cx, cy, scale, brush, black, outline, outSize, gap, 8, 1.5, true, false, false);
                break;

            case "t_shape":
                DrawCrossLines(canvas, cx, cy, scale, brush, black, outline, outSize, gap, 8, 1.5, false, true, false);
                break;

            case "cross_circle":
                DrawCrossLines(canvas, cx, cy, scale, brush, black, outline, outSize, gap, 8, 1.5, false, false, true);
                break;

            case "small_plus":
                DrawCrossLines(canvas, cx, cy, scale, brush, black, outline, outSize, 0, 5, 1.5, false, false, false);
                break;

            case "large_plus":
                DrawCrossLines(canvas, cx, cy, scale, brush, black, outline, outSize, 0, 12, 1.5, false, false, false);
                break;

            case "sniper":
                DrawCrossLines(canvas, cx, cy, scale, brush, black, outline, outSize, gap * 5, 40, 1.0, false, false, false);
                break;

            case "x_cross":
                DrawXLines(canvas, cx, cy, scale, brush, black, outline, outSize, 8, 1.5, false);
                break;

            case "x_dot":
                DrawXLines(canvas, cx, cy, scale, brush, black, outline, outSize, 8, 1.5, true);
                break;

            case "inward_arrows":
                DrawInwardArrows(canvas, cx, cy, scale, brush, black, outline, outSize);
                break;

            case "outward_chevrons":
                DrawOutwardChevrons(canvas, cx, cy, scale, brush, black, outline, outSize);
                break;

            case "triangle":
                DrawTriangle(canvas, cx, cy, scale, brush, black, outline, outSize);
                break;

            case "diamond":
                DrawDiamond(canvas, cx, cy, scale, brush, black, outline, outSize);
                break;

            case "dot_ring":
            {
                double r  = 6   * scale;
                double st = 1.5 * scale;
                if (outline) PutEllipseStroked(canvas, cx, cy, r, st + outSize * 2, black);
                PutEllipseStroked(canvas, cx, cy, r, st, brush);
                if (outline) PutEllipseFilled(canvas, cx, cy, 2 * scale + outSize, black);
                PutEllipseFilled(canvas, cx, cy, 2 * scale, brush);
                break;
            }

            case "double_ring":
            {
                double st = 1.2 * scale;
                if (outline) PutEllipseStroked(canvas, cx, cy, 4 * scale, st + outSize * 2, black);
                if (outline) PutEllipseStroked(canvas, cx, cy, 8 * scale, st + outSize * 2, black);
                PutEllipseStroked(canvas, cx, cy, 4 * scale, st, brush);
                PutEllipseStroked(canvas, cx, cy, 8 * scale, st, brush);
                break;
            }

            case "plus_dot":
                DrawCrossLines(canvas, cx, cy, scale, brush, black, outline, outSize, gap, 10, 2.0, true, false, false);
                break;

            case "brackets":
                DrawBrackets(canvas, cx, cy, scale, brush, black, outline, outSize);
                break;

            case "x_thick":
                DrawXLines(canvas, cx, cy, scale, brush, black, outline, outSize, 9, 3.0, false);
                break;
        }
    }

    static void DrawBrackets(Canvas c, double cx, double cy, double s,
        Brush brush, Brush black, bool outline, double outSize)
    {
        double g   = 7   * s;
        double len = 5   * s;
        double t   = 1.5 * s;

        var corners = new (double dx, double dy)[] { (-1, -1), (1, -1), (-1, 1), (1, 1) };
        foreach (var (dx, dy) in corners)
        {
            double x = cx + dx * g;
            double y = cy + dy * g;
            if (outline)
            {
                PutLine(c, x, y, x - dx * len, y, black, t + outSize * 2);
                PutLine(c, x, y, x, y - dy * len, black, t + outSize * 2);
            }
            PutLine(c, x, y, x - dx * len, y, brush, t);
            PutLine(c, x, y, x, y - dy * len, brush, t);
        }
    }

    static void DrawCrossLines(Canvas c, double cx, double cy, double s,
        Brush brush, Brush black, bool outline, double outSize,
        double gap, double len, double thick,
        bool withDot, bool noTop, bool withCircle)
    {
        double g = gap  * s;
        double l = len  * s;
        double t = thick * s;

        var arms = new List<(double x1, double y1, double x2, double y2)>();
        if (!noTop) arms.Add((cx, cy - g, cx, cy - g - l));
        arms.Add((cx, cy + g, cx, cy + g + l));
        arms.Add((cx - g, cy, cx - g - l, cy));
        arms.Add((cx + g, cy, cx + g + l, cy));

        if (outline)
            foreach (var (x1, y1, x2, y2) in arms)
                PutLine(c, x1, y1, x2, y2, black, t + outSize * 2);
        foreach (var (x1, y1, x2, y2) in arms)
            PutLine(c, x1, y1, x2, y2, brush, t);

        if (withDot)
        {
            double dr = 2.5 * s;
            if (outline) PutEllipseFilled(c, cx, cy, dr + outSize, black);
            PutEllipseFilled(c, cx, cy, dr, brush);
        }

        if (withCircle)
        {
            double cr = 12 * s;
            double cs = 1.5 * s;
            if (outline) PutEllipseStroked(c, cx, cy, cr, cs + outSize * 2, black);
            PutEllipseStroked(c, cx, cy, cr, cs, brush);
        }
    }

    static void DrawXLines(Canvas c, double cx, double cy, double s,
        Brush brush, Brush black, bool outline, double outSize,
        double len, double thick, bool withDot)
    {
        double l = len   * s;
        double t = thick * s;

        if (outline)
        {
            PutLine(c, cx - l, cy - l, cx + l, cy + l, black, t + outSize * 2);
            PutLine(c, cx + l, cy - l, cx - l, cy + l, black, t + outSize * 2);
        }
        PutLine(c, cx - l, cy - l, cx + l, cy + l, brush, t);
        PutLine(c, cx + l, cy - l, cx - l, cy + l, brush, t);

        if (withDot)
        {
            double dr = 2.5 * s;
            if (outline) PutEllipseFilled(c, cx, cy, dr + outSize, black);
            PutEllipseFilled(c, cx, cy, dr, brush);
        }
    }

    static void DrawInwardArrows(Canvas c, double cx, double cy, double s,
        Brush brush, Brush black, bool outline, double outSize)
    {
        double gap = 8 * s;
        double aw  = 8 * s;
        double ah  = 7 * s;

        var groups = new Point[][]
        {
            new[] { new Point(cx, cy - gap),      new Point(cx - aw / 2, cy - gap - ah), new Point(cx + aw / 2, cy - gap - ah) },
            new[] { new Point(cx, cy + gap),      new Point(cx - aw / 2, cy + gap + ah), new Point(cx + aw / 2, cy + gap + ah) },
            new[] { new Point(cx - gap, cy),      new Point(cx - gap - ah, cy - aw / 2), new Point(cx - gap - ah, cy + aw / 2) },
            new[] { new Point(cx + gap, cy),      new Point(cx + gap + ah, cy - aw / 2), new Point(cx + gap + ah, cy + aw / 2) }
        };

        foreach (var pts in groups)
        {
            if (outline)
            {
                var pOut = new Polygon { Stroke = black, StrokeThickness = outSize * 2, Fill = black, SnapsToDevicePixels = true };
                RenderOptions.SetEdgeMode(pOut, EdgeMode.Aliased);
                foreach (var p in pts) pOut.Points.Add(new Point(Math.Round(p.X), Math.Round(p.Y)));
                c.Children.Add(pOut);
            }
            var poly = new Polygon { Fill = brush, StrokeThickness = 0, SnapsToDevicePixels = true };
            RenderOptions.SetEdgeMode(poly, EdgeMode.Aliased);
            foreach (var p in pts) poly.Points.Add(new Point(Math.Round(p.X), Math.Round(p.Y)));
            c.Children.Add(poly);
        }
    }

    static void DrawOutwardChevrons(Canvas c, double cx, double cy, double s,
        Brush brush, Brush black, bool outline, double outSize)
    {
        double gap   = 5 * s;
        double chevW = 6 * s;
        double chevH = 5 * s;
        double t     = 1.5 * s;

        var chevrons = new (Point apex, Point p1, Point p2)[]
        {
            (new Point(cx,             cy - gap - chevH), new Point(cx - chevW, cy - gap),        new Point(cx + chevW, cy - gap)),
            (new Point(cx,             cy + gap + chevH), new Point(cx - chevW, cy + gap),        new Point(cx + chevW, cy + gap)),
            (new Point(cx - gap - chevH, cy),             new Point(cx - gap,   cy - chevW),      new Point(cx - gap,   cy + chevW)),
            (new Point(cx + gap + chevH, cy),             new Point(cx + gap,   cy - chevW),      new Point(cx + gap,   cy + chevW))
        };

        foreach (var (apex, p1, p2) in chevrons)
        {
            if (outline)
            {
                PutLine(c, apex.X, apex.Y, p1.X, p1.Y, black, t + outSize * 2);
                PutLine(c, apex.X, apex.Y, p2.X, p2.Y, black, t + outSize * 2);
            }
            PutLine(c, apex.X, apex.Y, p1.X, p1.Y, brush, t);
            PutLine(c, apex.X, apex.Y, p2.X, p2.Y, brush, t);
        }
    }

    static void DrawTriangle(Canvas c, double cx, double cy, double s,
        Brush brush, Brush black, bool outline, double outSize)
    {
        double h  = 18 * s;
        double w  = 16 * s;
        double st = Thick(1.5 * s);

        var pts = new[]
        {
            new Point(cx,         cy - 2 * h / 3),
            new Point(cx - w / 2, cy + h / 3),
            new Point(cx + w / 2, cy + h / 3)
        };

        if (outline)
        {
            var pOut = new Polygon { Stroke = black, StrokeThickness = Thick(st + outSize * 2), Fill = Brushes.Transparent, SnapsToDevicePixels = true };
            RenderOptions.SetEdgeMode(pOut, EdgeMode.Aliased);
            foreach (var p in pts) pOut.Points.Add(new Point(Math.Round(p.X), Math.Round(p.Y)));
            c.Children.Add(pOut);
        }
        var poly = new Polygon { Stroke = brush, StrokeThickness = st, Fill = Brushes.Transparent, SnapsToDevicePixels = true };
        RenderOptions.SetEdgeMode(poly, EdgeMode.Aliased);
        foreach (var p in pts) poly.Points.Add(new Point(Math.Round(p.X), Math.Round(p.Y)));
        c.Children.Add(poly);
    }

    static void DrawDiamond(Canvas c, double cx, double cy, double s,
        Brush brush, Brush black, bool outline, double outSize)
    {
        double r  = System.Math.Max(1.0, 10 * s);
        double st = Thick(1.5 * s);

        var pts = new[]
        {
            new Point(cx,     cy - r),
            new Point(cx + r, cy),
            new Point(cx,     cy + r),
            new Point(cx - r, cy)
        };

        if (outline)
        {
            var pOut = new Polygon { Stroke = black, StrokeThickness = Thick(st + outSize * 2), Fill = Brushes.Transparent, SnapsToDevicePixels = true };
            RenderOptions.SetEdgeMode(pOut, EdgeMode.Aliased);
            foreach (var p in pts) pOut.Points.Add(new Point(Math.Round(p.X), Math.Round(p.Y)));
            c.Children.Add(pOut);
        }
        var poly = new Polygon { Stroke = brush, StrokeThickness = st, Fill = Brushes.Transparent, SnapsToDevicePixels = true };
        RenderOptions.SetEdgeMode(poly, EdgeMode.Aliased);
        foreach (var p in pts) poly.Points.Add(new Point(Math.Round(p.X), Math.Round(p.Y)));
        c.Children.Add(poly);
    }

    static double Thick(double t) => System.Math.Max(0.5, t);

    static void PutLine(Canvas c, double x1, double y1, double x2, double y2, Brush stroke, double thick)
    {
        var line = new Line
        {
            X1 = Math.Round(x1), Y1 = Math.Round(y1),
            X2 = Math.Round(x2), Y2 = Math.Round(y2),
            Stroke              = stroke,
            StrokeThickness     = Thick(thick),
            StrokeStartLineCap  = PenLineCap.Square,
            StrokeEndLineCap    = PenLineCap.Square,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetEdgeMode(line, EdgeMode.Aliased);
        c.Children.Add(line);
    }

    static void PutEllipseFilled(Canvas c, double cx, double cy, double r, Brush fill)
    {
        r = System.Math.Max(0.5, r);
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, SnapsToDevicePixels = true };
        RenderOptions.SetEdgeMode(e, EdgeMode.Aliased);
        Canvas.SetLeft(e, Math.Round(cx - r));
        Canvas.SetTop(e,  Math.Round(cy - r));
        c.Children.Add(e);
    }

    static void PutEllipseStroked(Canvas c, double cx, double cy, double r, double thick, Brush stroke)
    {
        r = System.Math.Max(1.0, r);
        var e = new Ellipse
        {
            Width           = r * 2,
            Height          = r * 2,
            Stroke          = stroke,
            StrokeThickness = Thick(System.Math.Min(thick, r)),
            Fill            = Brushes.Transparent,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetEdgeMode(e, EdgeMode.Aliased);
        Canvas.SetLeft(e, Math.Round(cx - r));
        Canvas.SetTop(e,  Math.Round(cy - r));
        c.Children.Add(e);
    }

    static void PutRect(Canvas c, double cx, double cy, double hw, double hh, Brush fill)
    {
        hw = System.Math.Max(0.5, hw);
        hh = System.Math.Max(0.5, hh);
        var r = new Rectangle { Width = hw * 2, Height = hh * 2, Fill = fill, SnapsToDevicePixels = true };
        RenderOptions.SetEdgeMode(r, EdgeMode.Aliased);
        Canvas.SetLeft(r, Math.Round(cx - hw));
        Canvas.SetTop(r,  Math.Round(cy - hh));
        c.Children.Add(r);
    }
}