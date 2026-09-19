using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SentinelX;
public sealed class GlobalHotkeyService : IDisposable
{
    private readonly HwndSource source;
    private readonly Action action;
    private readonly Action? emergency;
    private const int HotkeyId = 778;
    private const int EmergencyId = 779;
    public bool Registered { get; }
    public bool EmergencyRegistered { get; }
    public GlobalHotkeyService(Window window, Action action, Action? emergency = null)
    {
        this.action = action; this.emergency = emergency; source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source.AddHook(Hook);
        Registered = RegisterHotKey(source.Handle, HotkeyId, 0x0002 | 0x0004 | 0x4000, 0x53);
        EmergencyRegistered = emergency != null && RegisterHotKey(source.Handle, EmergencyId, 0x0002 | 0x0004 | 0x4000, 0x58);
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    { if (msg == 0x0312 && wParam.ToInt32() == HotkeyId) { handled = true; action(); } if (msg == 0x0312 && wParam.ToInt32() == EmergencyId) { handled = true; emergency?.Invoke(); } return IntPtr.Zero; }
    public void Dispose() { if (Registered) UnregisterHotKey(source.Handle, HotkeyId); if (EmergencyRegistered) UnregisterHotKey(source.Handle, EmergencyId); source.RemoveHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr handle, int id);
}
