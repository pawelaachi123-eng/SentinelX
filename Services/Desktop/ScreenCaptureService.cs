using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SentinelX.Services.Desktop;

/// <summary>Live screen capture for collaboration mode. Captures all monitors (virtual screen) or a single
/// monitor, provides WPF BitmapSource for UI preview, and optionally saves JPEGs locally for audit.
/// No network — everything stays in %LOCALAPPDATA%\SentinelX\ScreenCaptures\ if enabled.</summary>
public interface IScreenCaptureService
{
    IReadOnlyList<MonitorInfo> Monitors { get; }
    event Action<BitmapSource>? FrameCaptured;
    bool IsLive { get; }
    void StartLive(int fps = 2);
    void StopLive();
    Task<BitmapSource?> CaptureAllAsync();
    Task<BitmapSource?> CaptureMonitorAsync(int monitorIndex);
    Task<string?> CaptureAndSaveAsync(int? monitorIndex = null);
}

public sealed record MonitorInfo(int Index, string Name, int X, int Y, int Width, int Height, bool Primary);

public sealed class ScreenCaptureService : IScreenCaptureService, IDisposable
{
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hmon, ref MONITORINFO lpmi);
    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    private readonly object sync = new();
    private System.Threading.Timer? timer;
    private int fps = 2;
    private bool disposed;

    public IReadOnlyList<MonitorInfo> Monitors { get; }

    public event Action<BitmapSource>? FrameCaptured;
    public bool IsLive { get; private set; }

    public ScreenCaptureService()
    {
        Monitors = EnumerateMonitors();
    }

    private static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var list = new List<MonitorInfo>();
        try
        {
            int idx = 0;
            bool Callback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    int w = mi.rcMonitor.right - mi.rcMonitor.left;
                    int h = mi.rcMonitor.bottom - mi.rcMonitor.top;
                    bool primary = (mi.dwFlags & 1) != 0;
                    list.Add(new MonitorInfo(idx, $"MONITOR_{idx}", mi.rcMonitor.left, mi.rcMonitor.top, w, h, primary));
                    idx++;
                }
                return true;
            }
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        }
        catch { }
        if (list.Count == 0)
        {
            list.Add(new MonitorInfo(0, "Primary", 0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, true));
        }
        return list;
    }

    public void StartLive(int fps = 2)
    {
        lock (sync)
        {
            if (disposed) return;
            this.fps = Math.Clamp(fps, 1, 10);
            StopLiveLocked();
            IsLive = true;
            int interval = 1000 / this.fps;
            timer = new System.Threading.Timer(_ => _ = CaptureAndPublishAsync(), null, 0, interval);
        }
    }

    public void StopLive()
    {
        lock (sync) StopLiveLocked();
    }

    private void StopLiveLocked()
    {
        timer?.Dispose();
        timer = null;
        IsLive = false;
    }

    private async Task CaptureAndPublishAsync()
    {
        try
        {
            var bmp = await CaptureAllAsync();
            if (bmp != null) FrameCaptured?.Invoke(bmp);
        }
        catch { /* live preview must never crash */ }
    }

    public async Task<BitmapSource?> CaptureAllAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                int x = (int)SystemParameters.VirtualScreenLeft;
                int y = (int)SystemParameters.VirtualScreenTop;
                int w = (int)SystemParameters.VirtualScreenWidth;
                int h = (int)SystemParameters.VirtualScreenHeight;
                if (w <= 0 || h <= 0) return null;
                using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
                }
                return ToBitmapSource(bitmap);
            }
            catch { return null; }
        });
    }

    public async Task<BitmapSource?> CaptureMonitorAsync(int monitorIndex)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (monitorIndex < 0 || monitorIndex >= Monitors.Count) monitorIndex = 0;
                var m = Monitors[monitorIndex];
                using var bitmap = new Bitmap(m.Width, m.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(m.X, m.Y, 0, 0, new System.Drawing.Size(m.Width, m.Height), CopyPixelOperation.SourceCopy);
                }
                return ToBitmapSource(bitmap);
            }
            catch { return null; }
        });
    }

    public async Task<string?> CaptureAndSaveAsync(int? monitorIndex = null)
    {
        try
        {
            BitmapSource? src = monitorIndex.HasValue ? await CaptureMonitorAsync(monitorIndex.Value) : await CaptureAllAsync();
            if (src == null) return null;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentinelX", "ScreenCaptures");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
            await Task.Run(() =>
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 70 };
                encoder.Frames.Add(BitmapFrame.Create(src));
                using var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
                encoder.Save(fs);
            });
            // Trim old files
            try
            {
                var files = Directory.GetFiles(dir, "*.jpg").OrderByDescending(f => f).Skip(50).ToArray();
                foreach (var f in files) File.Delete(f);
            }
            catch { }
            return file;
        }
        catch { return null; }
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            StopLiveLocked();
        }
    }
}
