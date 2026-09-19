using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace SentinelX
{
    public sealed class TrayService : IDisposable
    {
        // =========================================================
        // SENTINEL X
        // NATIVE WINDOWS SYSTEM TRAY
        // =========================================================


        // =========================================================
        // WINDOWS MESSAGES
        // =========================================================

        private const int WM_USER =
            0x0400;

        private const int WM_TRAYICON =
            WM_USER + 77;

        private const int WM_LBUTTONDBLCLK =
            0x0203;

        private const int WM_RBUTTONUP =
            0x0205;


        // =========================================================
        // SHELL NOTIFY ICON
        // =========================================================

        private const uint NIM_ADD =
            0x00000000;

        private const uint NIM_MODIFY =
            0x00000001;

        private const uint NIM_DELETE =
            0x00000002;

        private const uint NIF_MESSAGE =
            0x00000001;

        private const uint NIF_ICON =
            0x00000002;

        private const uint NIF_TIP =
            0x00000004;

        private const uint NIF_INFO =
            0x00000010;


        // =========================================================
        // ICON
        // =========================================================

        private const int IDI_APPLICATION =
            32512;


        // =========================================================
        // STATE
        // =========================================================

        private readonly HwndSource hwndSource;

        private readonly ContextMenu contextMenu;

        private readonly MenuItem openItem;
        private readonly MenuItem voiceOnItem;
        private readonly MenuItem voiceOffItem;
        private readonly MenuItem emergencyItem;
        private readonly MenuItem exitItem;

        private readonly IntPtr iconHandle;

        private bool disposed;

        private bool? lastVoiceState;
        private bool? lastEmergencyState;
        private bool? lastGamingState;


        // =========================================================
        // EVENTS
        // =========================================================

        public event Action? OpenRequested;

        public event Action? VoiceOnRequested;

        public event Action? VoiceOffRequested;

        public event Action? EmergencyStopRequested;

        public event Action? ExitRequested;


        // =========================================================
        // CONSTRUCTOR
        // =========================================================

        public TrayService()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "Sentinel X tray działa tylko na Windows.");
            }


            // -----------------------------------------------------
            // HIDDEN WINDOWS MESSAGE WINDOW
            // -----------------------------------------------------

            HwndSourceParameters parameters =
                new HwndSourceParameters(
                    "SentinelX.TrayMessageWindow")
                {
                    Width = 0,
                    Height = 0,
                    PositionX = 0,
                    PositionY = 0,
                    WindowStyle = 0
                };


            hwndSource =
                new HwndSource(
                    parameters);


            hwndSource.AddHook(
                WindowProcedure);


            // -----------------------------------------------------
            // WINDOWS DEFAULT ICON
            // -----------------------------------------------------

            iconHandle =
                LoadIcon(
                    IntPtr.Zero,
                    new IntPtr(
                        IDI_APPLICATION));


            // -----------------------------------------------------
            // CONTEXT MENU
            // -----------------------------------------------------

            openItem =
                new MenuItem
                {
                    Header =
                        "Otwórz Sentinel X"
                };


            voiceOnItem =
                new MenuItem
                {
                    Header =
                        "Voice ON"
                };


            voiceOffItem =
                new MenuItem
                {
                    Header =
                        "Voice OFF"
                };


            emergencyItem =
                new MenuItem
                {
                    Header =
                        "EMERGENCY STOP"
                };


            exitItem =
                new MenuItem
                {
                    Header =
                        "Zakończ Sentinel X"
                };


            openItem.Click +=
                OpenItem_Click;


            voiceOnItem.Click +=
                VoiceOnItem_Click;


            voiceOffItem.Click +=
                VoiceOffItem_Click;


            emergencyItem.Click +=
                EmergencyItem_Click;


            exitItem.Click +=
                ExitItem_Click;


            contextMenu =
                new ContextMenu();


            contextMenu.Items.Add(
                openItem);


            contextMenu.Items.Add(
                new Separator());


            contextMenu.Items.Add(
                voiceOnItem);


            contextMenu.Items.Add(
                voiceOffItem);


            contextMenu.Items.Add(
                new Separator());


            contextMenu.Items.Add(
                emergencyItem);


            contextMenu.Items.Add(
                new Separator());


            contextMenu.Items.Add(
                exitItem);


            // -----------------------------------------------------
            // CREATE ICON
            // -----------------------------------------------------

            AddTrayIcon();
        }


        // =========================================================
        // CREATE ICON
        // =========================================================

        private void AddTrayIcon()
        {
            NotifyIconData data =
                CreateNotifyIconData();


            data.Flags =
                NIF_MESSAGE |
                NIF_ICON |
                NIF_TIP;


            data.ToolTip =
                "Sentinel X";


            Shell_NotifyIcon(
                NIM_ADD,
                ref data);
        }


        // =========================================================
        // UPDATE STATE
        // =========================================================

        public void UpdateState(
            bool voiceOn,
            bool emergencyStop,
            bool gaming)
        {
            if (disposed)
                return;


            if (lastVoiceState == voiceOn &&
                lastEmergencyState == emergencyStop &&
                lastGamingState == gaming)
            {
                return;
            }


            lastVoiceState =
                voiceOn;


            lastEmergencyState =
                emergencyStop;


            lastGamingState =
                gaming;


            voiceOnItem.IsEnabled =
                !voiceOn &&
                !emergencyStop;


            voiceOffItem.IsEnabled =
                voiceOn;


            emergencyItem.IsEnabled =
                !emergencyStop;


            string tooltip;


            if (emergencyStop)
            {
                tooltip =
                    "Sentinel X - EMERGENCY STOP";
            }
            else if (gaming &&
                     voiceOn)
            {
                tooltip =
                    "Sentinel X - Gaming - Voice ON";
            }
            else if (gaming)
            {
                tooltip =
                    "Sentinel X - Gaming Mode";
            }
            else if (voiceOn)
            {
                tooltip =
                    "Sentinel X - Voice ON";
            }
            else
            {
                tooltip =
                    "Sentinel X - Ready";
            }


            UpdateToolTip(
                tooltip);
        }


        // =========================================================
        // TOOLTIP
        // =========================================================

        private void UpdateToolTip(
            string tooltip)
        {
            NotifyIconData data =
                CreateNotifyIconData();


            data.Flags =
                NIF_TIP;


            data.ToolTip =
                LimitLength(
                    tooltip,
                    127);


            Shell_NotifyIcon(
                NIM_MODIFY,
                ref data);
        }


        // =========================================================
        // NOTIFICATION
        // =========================================================

        public void ShowInfo(
            string title,
            string message)
        {
            if (disposed)
                return;


            try
            {
                NotifyIconData data =
                    CreateNotifyIconData();


                data.Flags =
                    NIF_INFO;


                data.InfoTitle =
                    LimitLength(
                        title,
                        63);


                data.Info =
                    LimitLength(
                        message,
                        255);


                data.InfoFlags =
                    1;


                data.TimeoutOrVersion =
                    2500;


                Shell_NotifyIcon(
                    NIM_MODIFY,
                    ref data);
            }
            catch
            {
                // Tray notification is optional.
            }
        }


        // =========================================================
        // WINDOWS MESSAGE HANDLER
        // =========================================================

        private IntPtr WindowProcedure(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message !=
                WM_TRAYICON)
            {
                return IntPtr.Zero;
            }


            int mouseMessage =
                unchecked(
                    (int)lParam.ToInt64());


            if (mouseMessage ==
                WM_LBUTTONDBLCLK)
            {
                handled =
                    true;


                OpenRequested?.Invoke();


                return IntPtr.Zero;
            }


            if (mouseMessage ==
                WM_RBUTTONUP)
            {
                handled =
                    true;


                ShowContextMenu();


                return IntPtr.Zero;
            }


            return IntPtr.Zero;
        }


        // =========================================================
        // MENU
        // =========================================================

        private void ShowContextMenu()
        {
            Application.Current.Dispatcher.Invoke(
                () =>
                {
                    contextMenu.Placement =
                        PlacementMode.MousePoint;


                    contextMenu.IsOpen =
                        true;
                });
        }


        private void OpenItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenRequested?.Invoke();
        }


        private void VoiceOnItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            VoiceOnRequested?.Invoke();
        }


        private void VoiceOffItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            VoiceOffRequested?.Invoke();
        }


        private void EmergencyItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            EmergencyStopRequested?.Invoke();
        }


        private void ExitItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ExitRequested?.Invoke();
        }


        // =========================================================
        // NOTIFY ICON STRUCT
        // =========================================================

        private NotifyIconData CreateNotifyIconData()
        {
            return new NotifyIconData
            {
                Size =
                    Marshal.SizeOf<NotifyIconData>(),

                WindowHandle =
                    hwndSource.Handle,

                IconId =
                    77,

                CallbackMessage =
                    WM_TRAYICON,

                IconHandle =
                    iconHandle,

                ToolTip =
                    string.Empty,

                Info =
                    string.Empty,

                InfoTitle =
                    string.Empty
            };
        }


        // =========================================================
        // STRING LIMIT
        // =========================================================

        private static string LimitLength(
            string text,
            int maximum)
        {
            if (string.IsNullOrEmpty(
                    text))
            {
                return string.Empty;
            }


            if (text.Length <= maximum)
            {
                return text;
            }


            return text.Substring(
                0,
                maximum);
        }


        // =========================================================
        // DISPOSE
        // =========================================================

        public void Dispose()
        {
            if (disposed)
                return;


            disposed =
                true;


            try
            {
                NotifyIconData data =
                    CreateNotifyIconData();


                Shell_NotifyIcon(
                    NIM_DELETE,
                    ref data);
            }
            catch
            {
            }


            try
            {
                openItem.Click -=
                    OpenItem_Click;


                voiceOnItem.Click -=
                    VoiceOnItem_Click;


                voiceOffItem.Click -=
                    VoiceOffItem_Click;


                emergencyItem.Click -=
                    EmergencyItem_Click;


                exitItem.Click -=
                    ExitItem_Click;
            }
            catch
            {
            }


            try
            {
                contextMenu.IsOpen =
                    false;
            }
            catch
            {
            }


            try
            {
                hwndSource.RemoveHook(
                    WindowProcedure);


                hwndSource.Dispose();
            }
            catch
            {
            }
        }


        // =========================================================
        // WIN32
        // =========================================================

        [DllImport(
            "shell32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern bool Shell_NotifyIcon(
            uint message,
            ref NotifyIconData data);


        [DllImport(
            "user32.dll",
            CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIcon(
            IntPtr instance,
            IntPtr iconName);


        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public int Size;

            public IntPtr WindowHandle;

            public uint IconId;

            public uint Flags;

            public int CallbackMessage;

            public IntPtr IconHandle;


            [MarshalAs(
                UnmanagedType.ByValTStr,
                SizeConst = 128)]
            public string ToolTip;


            public uint State;

            public uint StateMask;


            [MarshalAs(
                UnmanagedType.ByValTStr,
                SizeConst = 256)]
            public string Info;


            public uint TimeoutOrVersion;


            [MarshalAs(
                UnmanagedType.ByValTStr,
                SizeConst = 64)]
            public string InfoTitle;


            public uint InfoFlags;


            public Guid GuidItem;


            public IntPtr BalloonIconHandle;
        }
    }
}