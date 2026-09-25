using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SentinelX;
using SentinelX.Core;

namespace SentinelX.Services.Desktop;

/// <summary>Second cursor in a different color — the user's real mouse stays, Sentinel's cursor is an
/// overlay (topmost, transparent) that shows where AI will act. When collaboration is active, the
/// service can also move the real mouse and click (full control), but only after explicit UI button
/// activation and with a visible red border indicator. Emergency stop (Ctrl+Shift+X) disables it.</summary>
public interface ICoPilotCursorService
{
    bool IsActive { get; }
    event Action? StateChanged;
    event Action<Point>? PositionChanged;
    Point Position { get; }
    string Status { get; }
    void Enable();
    void Disable();
    void MoveTo(int x, int y);
    void MoveTo(Point point);
    void Click();
    void RightClick();
    void DoubleClick();
    void Scroll(int delta);
}

public sealed class CoPilotCursorService : ICoPilotCursorService, IDisposable
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    private const uint MOUSEEVENTF_LEFTUP = 0x04;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
    private const uint MOUSEEVENTF_RIGHTUP = 0x10;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private readonly object sync = new();
    private readonly IUiDispatcher dispatcher;
    private CoPilotOverlayWindow? overlay;
    private CoPilotBorderWindow? border;
    private bool disposed;
    private Point position = new(0, 0);

    public bool IsActive { get; private set; }
    public Point Position { get { lock (sync) return position; } }
    public string Status { get; private set; } = "Wyłączony";
    public event Action? StateChanged;
    public event Action<Point>? PositionChanged;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    public CoPilotCursorService(IUiDispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
    }

    public void Enable()
    {
        lock (sync)
        {
            if (disposed || IsActive) return;
            IsActive = true;
            Status = "AKTYWNY — drugi kursor (niebieski) widoczny, kontrola myszki włączona";
        }
        dispatcher.Post(() =>
        {
            try
            {
                overlay = new CoPilotOverlayWindow();
                overlay.Show();
                border = new CoPilotBorderWindow();
                border.Show();
                // Start at current mouse pos
                if (GetCursorPos(out POINT pt)) MoveTo(pt.X, pt.Y);
            }
            catch { }
        });
        StateChanged?.Invoke();
        AppLog.Write("CoPilot cursor enabled");
    }

    public void Disable()
    {
        lock (sync)
        {
            if (!IsActive) return;
            IsActive = false;
            Status = "Wyłączony";
        }
        dispatcher.Post(() =>
        {
            try { overlay?.Close(); overlay = null; } catch { }
            try { border?.Close(); border = null; } catch { }
        });
        StateChanged?.Invoke();
        AppLog.Write("CoPilot cursor disabled");
    }

    public void MoveTo(int x, int y) => MoveTo(new Point(x, y));

    public void MoveTo(Point point)
    {
        lock (sync) position = point;
        PositionChanged?.Invoke(point);
        dispatcher.Post(() =>
        {
            try { overlay?.UpdatePosition(point); } catch { }
        });
        // Move real mouse only when active — this is the „full control” the user asked for.
        if (IsActive)
        {
            try { SetCursorPos((int)point.X, (int)point.Y); } catch { }
        }
    }

    public void Click()
    {
        if (!IsActive) return;
        try
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            AppLog.Write($"CoPilot click at {Position}");
        }
        catch { }
    }

    public void RightClick()
    {
        if (!IsActive) return;
        try
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }
        catch { }
    }

    public void DoubleClick()
    {
        if (!IsActive) return;
        Click();
        Thread.Sleep(60);
        Click();
    }

    public void Scroll(int delta)
    {
        if (!IsActive) return;
        try { mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, UIntPtr.Zero); } catch { }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
        }
        Disable();
    }
}

/// <summary>Transparent topmost window showing Sentinel's second cursor in a different color (cyan by default).
/// The user's real cursor stays — this is an additional visual indicator.</summary>
public sealed class CoPilotOverlayWindow : Window
{
    private readonly System.Windows.Shapes.Ellipse outer;
    private readonly System.Windows.Shapes.Ellipse inner;
    private readonly System.Windows.Controls.TextBlock label;

    public CoPilotOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Width = 48; Height = 48;
        IsHitTestVisible = false;
        // Make click-through
        Loaded += (_, _) =>
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int exStyle = (int)NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch { }
        };

        var grid = new System.Windows.Controls.Grid();
        outer = new System.Windows.Shapes.Ellipse
        {
            Width = 36, Height = 36,
            Stroke = (Brush)new BrushConverter().ConvertFromString("#00D4FF")!,
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Color.FromArgb(60, 0, 212, 255))
        };
        inner = new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8,
            Fill = (Brush)new BrushConverter().ConvertFromString("#00D4FF")!
        };
        label = new System.Windows.Controls.TextBlock
        {
            Text = "S",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(outer);
        grid.Children.Add(inner);
        grid.Children.Add(label);
        Content = grid;
    }

    public void UpdatePosition(Point p)
    {
        Left = p.X - Width / 2;
        Top = p.Y - Height / 2;
    }

    private static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        [DllImport("user32.dll")] public static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}

/// <summary>Red border around all screens when collaboration is active — visible indicator that
/// screen is being viewed and mouse can be controlled.</summary>
public sealed class CoPilotBorderWindow : Window
{
    public CoPilotBorderWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        IsHitTestVisible = false;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var grid = new System.Windows.Controls.Grid();
        var borderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 59, 48)); // red
        var thickness = new Thickness(4);
        // Top
        grid.Children.Add(new System.Windows.Controls.Border { Background = borderBrush, Height = 4, VerticalAlignment = VerticalAlignment.Top });
        // Bottom
        grid.Children.Add(new System.Windows.Controls.Border { Background = borderBrush, Height = 4, VerticalAlignment = VerticalAlignment.Bottom });
        // Left
        grid.Children.Add(new System.Windows.Controls.Border { Background = borderBrush, Width = 4, HorizontalAlignment = HorizontalAlignment.Left });
        // Right
        grid.Children.Add(new System.Windows.Controls.Border { Background = borderBrush, Width = 4, HorizontalAlignment = HorizontalAlignment.Right });

        var indicator = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 255, 59, 48)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 0, 0),
            Child = new System.Windows.Controls.TextBlock
            {
                Text = "● TRYB WSPÓŁPRACY AKTYWNY — Ekran widoczny, myszka sterowana",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12
            }
        };
        grid.Children.Add(indicator);
        Content = grid;

        Loaded += (_, _) =>
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int exStyle = (int)NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch { }
        };
    }

    private static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        [DllImport("user32.dll")] public static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
