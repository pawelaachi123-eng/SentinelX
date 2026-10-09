using System.Runtime.InteropServices;

namespace SentinelX.WinUI.Services.Hotkeys;

/// <summary>
/// Global hotkeys over a message-only window: Ctrl+Shift+S shows Sentinel,
/// Ctrl+Shift+X is the emergency stop. Same bindings as the WPF build.
/// </summary>
public sealed class WinUiHotkeyService : IDisposable
{
    public event Action? ShowRequested;
    public event Action? EmergencyRequested;

    private const int HotkeyShowId = 778;
    private const int HotkeyEmergencyId = 779;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint KeyS = 0x53;
    private const uint KeyX = 0x58;

    private static readonly IntPtr HwndMessage = new(-3);

    private readonly WndProc wndProc;
    private readonly IntPtr window;
    private readonly ushort windowClass;
    private bool disposed;

    public bool Registered { get; }
    public bool EmergencyRegistered { get; }

    public WinUiHotkeyService()
    {
        wndProc = WindowProcedure;
        IntPtr instance = GetModuleHandle(null);
        var cls = new WNDCLASS
        {
            lpfnWndProc = wndProc,
            hInstance = instance,
            lpszClassName = "SentinelX.WinUI.HotkeyWindow"
        };
        windowClass = RegisterClassW(ref cls);
        if (windowClass == 0)
            throw new InvalidOperationException("Nie można zarejestrować okna skrótów klawiszowych.");

        window = CreateWindowExW(0, cls.lpszClassName, "", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
        if (window == IntPtr.Zero)
            throw new InvalidOperationException("Nie można utworzyć okna skrótów klawiszowych.");

        Registered = RegisterHotKey(window, HotkeyShowId, ModControl | ModShift | ModNoRepeat, KeyS);
        EmergencyRegistered = RegisterHotKey(window, HotkeyEmergencyId, ModControl | ModShift | ModNoRepeat, KeyX);
    }

    private IntPtr WindowProcedure(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey)
        {
            int id = wParam.ToInt32();
            try
            {
                if (id == HotkeyShowId) ShowRequested?.Invoke();
                else if (id == HotkeyEmergencyId) EmergencyRequested?.Invoke();
            }
            catch (Exception ex)
            {
                SentinelX.AppLog.Write(ex);
            }

            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (Registered) UnregisterHotKey(window, HotkeyShowId);
        if (EmergencyRegistered) UnregisterHotKey(window, HotkeyEmergencyId);
        if (window != IntPtr.Zero) DestroyWindow(window);
        if (windowClass != 0) UnregisterClassW("SentinelX.WinUI.HotkeyWindow", GetModuleHandle(null));
        GC.KeepAlive(wndProc);
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
