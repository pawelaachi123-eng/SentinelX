using System.IO;
using System.Runtime.InteropServices;

namespace SentinelX.WinUI.Services.Tray;

/// <summary>
/// Native Windows system tray icon: message-only window + Shell_NotifyIcon + a classic
/// Win32 popup menu. No UI framework dependency — works the same under WinUI.
/// </summary>
public sealed class WinUiTrayService : IDisposable
{
    public event Action? OpenRequested;
    public event Action? PhoneRequested;
    public event Action? VoiceOnRequested;
    public event Action? VoiceOffRequested;
    public event Action? MiniModeRequested;
    public event Action? EmergencyStopRequested;
    public event Action? ExitRequested;

    private const int WmUser = 0x0400;
    private const int WmTrayIcon = WmUser + 77;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int WmNull = 0x0000;

    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint NifInfo = 16;

    private const uint TpmReturnCmd = 0x0100;
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;

    private const int ImageIcon = 1;
    private const uint LrLoadFromFile = 0x10;
    private const int IdiApplication = 32512;

    private const int MenuOpen = 101;
    private const int MenuPhone = 102;
    private const int MenuVoiceOn = 103;
    private const int MenuVoiceOff = 104;
    private const int MenuMini = 105;
    private const int MenuEmergency = 106;
    private const int MenuExit = 107;

    private static readonly IntPtr HwndMessage = new(-3);

    private readonly WndProc wndProc;
    private readonly IntPtr window;
    private readonly IntPtr icon;
    private readonly ushort trayClass;
    private readonly uint taskbarCreated;
    private bool disposed;
    private bool added;

    public WinUiTrayService()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Zasobnik Sentinel X działa tylko na Windows.");

        wndProc = WindowProcedure;
        IntPtr instance = GetModuleHandle(null);
        var cls = new WNDCLASS
        {
            lpfnWndProc = wndProc,
            hInstance = instance,
            lpszClassName = "SentinelX.WinUI.TrayMessageWindow"
        };
        trayClass = RegisterClassW(ref cls);
        if (trayClass == 0)
            throw new InvalidOperationException("Nie można zarejestrować okna komunikatów zasobnika.");

        window = CreateWindowExW(0, cls.lpszClassName, "", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
        if (window == IntPtr.Zero)
            throw new InvalidOperationException("Nie można utworzyć okna komunikatów zasobnika.");

        icon = LoadTrayIcon();
        taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        var data = CreateData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = WmTrayIcon;
        data.hIcon = icon;
        data.szTip = "Sentinel X";
        added = Shell_NotifyIconW(NimAdd, ref data);
        if (!added)
            throw new InvalidOperationException("Nie można dodać ikony do zasobnika systemowego.");
    }

    private static IntPtr LoadTrayIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "sentinel.ico");
            if (File.Exists(path))
            {
                IntPtr loaded = LoadImageW(IntPtr.Zero, path, ImageIcon, 0, 0, LrLoadFromFile);
                if (loaded != IntPtr.Zero) return loaded;
            }
        }
        catch (Exception ex)
        {
            SentinelX.AppLog.Write(ex);
        }

        return LoadIconW(IntPtr.Zero, IdiApplication);
    }

    public void UpdateState(bool voiceOn, bool stopped, bool gaming)
    {
        if (disposed || !added) return;
        string tooltip = stopped ? "Sentinel X — STOP awaryjny"
            : gaming ? "Sentinel X — tryb gry"
            : voiceOn ? "Sentinel X — mikrofon włączony"
            : "Sentinel X — gotowy";
        try
        {
            var data = CreateData();
            data.uFlags = NifTip;
            data.szTip = tooltip;
            Shell_NotifyIconW(NimModify, ref data);
        }
        catch (Exception ex)
        {
            SentinelX.AppLog.Write(ex);
        }
    }

    public void ShowInfo(string title, string message)
    {
        if (disposed || !added) return;
        try
        {
            var data = CreateData();
            data.uFlags = NifInfo;
            data.szInfoTitle = Limit(title, 63);
            data.szInfo = Limit(message, 255);
            data.dwInfoFlags = 1;
            data.uTimeoutOrVersion = 2500;
            Shell_NotifyIconW(NimModify, ref data);
        }
        catch
        {
            // Tray balloon is optional.
        }
    }

    private NOTIFYICONDATA CreateData() => new() { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = window, uID = 1 };

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];

    private IntPtr WindowProcedure(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == taskbarCreated && taskbarCreated != 0)
        {
            var data = CreateData();
            data.uFlags = NifMessage | NifIcon | NifTip;
            data.uCallbackMessage = WmTrayIcon;
            data.hIcon = icon;
            data.szTip = "Sentinel X";
            Shell_NotifyIconW(NimAdd, ref data);
            return IntPtr.Zero;
        }

        if (message != WmTrayIcon) return DefWindowProcW(hWnd, message, wParam, lParam);

        int mouse = unchecked((int)lParam.ToInt64());
        if (mouse == WmLButtonUp || mouse == WmLButtonDblClk)
        {
            try
            {
                OpenRequested?.Invoke();
            }
            catch (Exception ex)
            {
                SentinelX.AppLog.Write(ex);
            }
        }
        else if (mouse == WmRButtonUp)
        {
            ShowMenu();
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenuW(menu, MfString, MenuOpen, "Otwórz Sentinel");
            AppendMenuW(menu, MfString, MenuPhone, "Telefon…");
            AppendMenuW(menu, MfSeparator, 0, "");
            AppendMenuW(menu, MfString, MenuVoiceOn, "Włącz głos");
            AppendMenuW(menu, MfString, MenuVoiceOff, "Wyłącz głos");
            AppendMenuW(menu, MfSeparator, 0, "");
            AppendMenuW(menu, MfString, MenuMini, "Tryb Mini");
            AppendMenuW(menu, MfSeparator, 0, "");
            AppendMenuW(menu, MfString, MenuEmergency, "STOP awaryjny");
            AppendMenuW(menu, MfString, MenuExit, "Wyjdź z aplikacji");

            GetCursorPos(out POINT point);
            SetForegroundWindow(window);
            int chosen = (int)TrackPopupMenuEx(menu, TpmReturnCmd, point.X, point.Y, window, IntPtr.Zero);
            PostMessageW(window, WmNull, IntPtr.Zero, IntPtr.Zero);

            Action? action = chosen switch
            {
                MenuOpen => OpenRequested,
                MenuPhone => PhoneRequested,
                MenuVoiceOn => VoiceOnRequested,
                MenuVoiceOff => VoiceOffRequested,
                MenuMini => MiniModeRequested,
                MenuEmergency => EmergencyStopRequested,
                MenuExit => ExitRequested,
                _ => null
            };
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                SentinelX.AppLog.Write(ex);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (added)
            {
                var data = CreateData();
                Shell_NotifyIconW(NimDelete, ref data);
                added = false;
            }
        }
        catch
        {
        }

        if (icon != IntPtr.Zero) DestroyIcon(icon);
        if (window != IntPtr.Zero) DestroyWindow(window);
        if (trayClass != 0) UnregisterClassW("SentinelX.WinUI.TrayMessageWindow", GetModuleHandle(null));
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, int type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, int lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);
}
