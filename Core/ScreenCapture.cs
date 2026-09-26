using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SentinelX.Core;

/// <summary>0.95 · zrzut ekranu: GDI BitBlt do bitmapy, kodowanie PNG przez WPF (bez WinForms,
/// bez pakietów NuGet). Plik trafia do „Screenshots” w folderze danych — nazwa z datą i godziną,
/// nigdy niczego nie nadpisuje. Przy braku pulpitu (sesja bez GUI) zwraca null — komenda
/// odpowiada uczciwie, że zrzut się nie udał.</summary>
public static class ScreenCapture
{
    /// <summary>Zapisuje zrzut całego wirtualnego pulpitu. Zwraca (ścieżka, szerokość, wysokość)
    /// albo null, gdy przechwycenie się nie udało.</summary>
    public static (string Path, int Width, int Height)? CaptureVirtualScreen()
    {
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        if (width <= 0 || height <= 0)
        {
            width = GetSystemMetrics(SM_CXSCREEN);
            height = GetSystemMetrics(SM_CYSCREEN);
            left = 0; top = 0;
        }
        if (width <= 0 || height <= 0) return null;

        IntPtr screenDC = IntPtr.Zero;
        IntPtr memoryDC = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            screenDC = GetDC(IntPtr.Zero);
            if (screenDC == IntPtr.Zero) return null;
            memoryDC = CreateCompatibleDC(screenDC);
            if (memoryDC == IntPtr.Zero) return null;
            bitmap = CreateCompatibleBitmap(screenDC, width, height);
            if (bitmap == IntPtr.Zero) return null;
            oldBitmap = SelectObject(memoryDC, bitmap);
            if (!BitBlt(memoryDC, 0, 0, width, height, screenDC, left, top, SRCCOPY)) return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero,
                new Int32Rect(0, 0, width, height), BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            string directory = Path.Combine(AppPaths.Root, "Screenshots");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "zrzut-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            // Unikalna nazwa nawet przy dwóch zrzutach w tej samej sekundzie.
            int suffix = 1;
            while (File.Exists(path)) path = Path.Combine(directory, "zrzut-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix++ + ".png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path)) encoder.Save(stream);
            return (path, width, height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return null; }
        finally
        {
            if (oldBitmap != IntPtr.Zero && memoryDC != IntPtr.Zero) SelectObject(memoryDC, oldBitmap);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDC != IntPtr.Zero) DeleteDC(memoryDC);
            if (screenDC != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private const int SRCCOPY = 0x00CC0020;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destDc, int x, int y, int width, int height, IntPtr srcDc, int srcX, int srcY, int rasterOp);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
