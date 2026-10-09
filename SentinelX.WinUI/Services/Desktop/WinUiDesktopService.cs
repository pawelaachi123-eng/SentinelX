using System.IO;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Link;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
using SentinelX.ViewModels;
using SentinelX.WinUI.Core;
using SentinelX.WinUI.Services.Hotkeys;
using SentinelX.WinUI.Services.Link;
using SentinelX.WinUI.Services.Toasts;
using SentinelX.WinUI.Services.Tray;
using SentinelX.WinUI.UI;
using Windows.Graphics;

namespace SentinelX.Services.Desktop;

/// <summary>Owns Windows interop and UI lifetime for the WinUI shell: tray, hotkeys,
/// autostart, theme, Watch thresholds, Mini Mode and the gaming overlay.</summary>
public sealed class WinUiDesktopService(
    ISettingsService settings,
    IActionEngine engine,
    IVoiceService voice,
    ISystemMonitorService monitor,
    IUiDispatcher dispatcher,
    AlertFeed alerts,
    WinUiLinkUi linkUi,
    ToastService toasts,
    GamingPolicyService gamingPolicy,
    IServiceProvider provider) : IDesktopService, IDisposable
{
    private Window? shell;
    private MiniModeWindow? mini;
    private GamingOverlayWindow? overlay;
    private WinUiTrayService? tray;
    private WinUiHotkeyService? hotkeys;
    private bool exiting;
    private bool disposed;
    private bool? startupApplied;
    private bool sizedOnce;
    private readonly StartupService startup = new("Sentinel X");
    private DateTime? highUsageSince;
    private DateTime lastAlert;

    public string Status { get; private set; } = "";
    public event Action? StatusChanged;

    public void Attach(Window target)
    {
        if (shell != null) return;
        shell = target;
        PhoneHint.Open = linkUi.ShowPhoneWindow;
        linkUi.ShowWindowAction = ShowWindow;
        shell.AppWindow.Closing += OnClosing;
        shell.AppWindow.Changed += OnShellChanged;
        settings.Changed += ApplySettings;
        monitor.Updated += OnMetrics;
        engine.Changed += OnState;
        voice.Changed += OnState;
        toasts.Activated += ShowWindow;
        ApplySettings();

        if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1")
        {
            monitor.Start();
            return;
        }

        try
        {
            hotkeys = new WinUiHotkeyService();
            hotkeys.ShowRequested += ShowWindow;
            hotkeys.EmergencyRequested += engine.EmergencyStop;
            if (!hotkeys.EmergencyRegistered) SetStatus("Ctrl+Shift+X jest zajęty. Użyj przycisku STOP.");
        }
        catch (Exception ex)
        {
            SetStatus("Nie udało się zarejestrować skrótów: " + ex.Message);
            AppLog.Write(ex);
        }

        try
        {
            tray = new WinUiTrayService();
            tray.OpenRequested += ShowWindow;
            tray.PhoneRequested += linkUi.ShowPhoneWindow;
            tray.VoiceOnRequested += StartVoice;
            tray.VoiceOffRequested += voice.Stop;
            tray.MiniModeRequested += ToggleMiniMode;
            tray.EmergencyStopRequested += engine.EmergencyStop;
            tray.ExitRequested += Exit;
            OnState();
        }
        catch (Exception ex)
        {
            SetStatus("Zasobnik niedostępny; zamknięcie zakończy aplikację. " + ex.Message);
            AppLog.Write(ex);
            tray = null;
        }

        monitor.Start();
        gamingPolicy.Start();

        // Started by Windows (autostart): stay quietly in the tray; the phone and the assistant work without a window.
        if ((settings.Current.Startup.StartMinimized || WinUI.App.AutostartLaunch) && tray != null)
            shell.AppWindow.Hide();
        if (settings.Current.Startup.StartVoiceOnLaunch)
            StartVoice();
    }

    private async void StartVoice()
    {
        if (voice.State != VoiceState.Off) return;
        try
        {
            await voice.StartAsync(settings.Current.Voice.SelectedMicrophoneDevice, false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            AppLog.Write(ex);
        }
    }

    private void OnShellChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange
            && sender.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized
            && settings.Current.Ui.MinimizeToTray && tray != null)
            shell?.AppWindow.Hide();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!exiting && settings.Current.Ui.CloseToTray && tray != null)
        {
            args.Cancel = true;
            shell?.AppWindow.Hide();
            return;
        }

        exiting = true;
        try
        {
            engine.EmergencyStop();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }

        try
        {
            voice.Stop();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }

        CloseMini();
        try
        {
            overlay?.Close();
        }
        catch
        {
        }

        try
        {
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }
    }

    private void OnState() => dispatcher.Post(() =>
        tray?.UpdateState(voice.State != VoiceState.Off, engine.IsStopped, !string.IsNullOrEmpty(monitor.Current.Game)));

    private void OnMetrics(SystemSnapshot snapshot) => dispatcher.Post(() =>
    {
        OnState();
        var watch = settings.Current.Watch;
        double ramPercent = snapshot.RamTotal > 0 ? snapshot.RamUsed / snapshot.RamTotal * 100 : double.NaN;
        bool high = watch.Enabled && (snapshot.Cpu >= watch.CpuAlertPercent || ramPercent >= watch.RamAlertPercent);
        if (!high)
        {
            highUsageSince = null;
        }
        else
        {
            highUsageSince ??= snapshot.Timestamp;
            if (snapshot.Timestamp - highUsageSince >= TimeSpan.FromSeconds(watch.MinSecondsBeforeAlert)
                && snapshot.Timestamp - lastAlert >= TimeSpan.FromMinutes(watch.CooldownMinutes))
            {
                lastAlert = snapshot.Timestamp;
                string message = $"CPU {snapshot.CpuText} · RAM {snapshot.RamText}";
                SetStatus("Watch: długotrwałe obciążenie. " + message);
                tray?.ShowInfo("Sentinel Watch", message);
                alerts.Add("warn", "Wysokie obciążenie komputera",
                    message + " — utrzymuje się dłużej niż " + watch.MinSecondsBeforeAlert + " s.");
            }
        }
    });

    private void ApplySettings()
    {
        if (shell == null || disposed) return;
        string theme = settings.Current.Ui.Theme;
        bool light = theme == "Light";
        if (theme == "System")
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                light = key?.GetValue("AppsUseLightTheme") is int v && v != 0;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }
        }

        try
        {
            ElementTheme requested = theme == "System" ? ElementTheme.Default : light ? ElementTheme.Light : ElementTheme.Dark;
            if (shell.Content is FrameworkElement root) root.RequestedTheme = requested;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }

        if (!sizedOnce)
        {
            sizedOnce = true;
            try
            {
                string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "sentinel.ico");
                if (File.Exists(icon)) shell.AppWindow.SetIcon(icon);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }

            try
            {
                int width = (int)Math.Clamp(settings.Current.Ui.WindowWidth, 1024, 2560);
                int height = (int)Math.Clamp(settings.Current.Ui.WindowHeight, 700, 1440);
                shell.AppWindow.Resize(new SizeInt32(width, height));
                var area = DisplayArea.GetFromWindowId(shell.AppWindow.Id, DisplayAreaFallback.Primary)?.WorkArea;
                if (area is { } work)
                    shell.AppWindow.Move(new PointInt32(
                        work.X + Math.Max(0, (work.Width - width) / 2),
                        work.Y + Math.Max(0, (work.Height - height) / 2)));
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }
        }

        ApplyMiniTopmost();
        if (overlay != null) ApplyOverlay();

        if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1") return;
        bool desired = settings.Current.Startup.StartWithWindows;
        if (startupApplied != desired)
        {
            bool ok = desired ? startup.Enable() : startup.Disable();
            if (ok) startupApplied = desired;
            else SetStatus("Nie udało się zastosować autostartu w rejestrze Windows.");
        }
    }

    public void ShowWindow() => dispatcher.Post(() =>
    {
        if (shell == null) return;
        CloseMini();
        if (shell.AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized)
            presenter.Restore();
        shell.AppWindow.Show();
        shell.Activate();
    });

    public void ToggleMiniMode() => dispatcher.Post(() =>
    {
        if (shell == null || exiting || disposed) return;
        if (mini != null)
        {
            ShowWindow();
            return;
        }

        try
        {
            mini = provider.GetRequiredService<MiniModeWindow>();
            mini.Closed += OnMiniClosed;
            mini.ViewModel.ExpandRequested += OnMiniExpand;
            ApplyMiniTopmost();
            var area = DisplayArea.GetFromWindowId(shell.AppWindow.Id, DisplayAreaFallback.Primary)?.WorkArea;
            if (area is { } work)
                mini.AppWindow.Move(new PointInt32(work.X + work.Width - 440, work.Y + work.Height - 400));
            shell.AppWindow.Hide();
            mini.Activate();
        }
        catch (Exception ex)
        {
            SetStatus("Nie można otworzyć trybu Mini: " + ex.Message);
            AppLog.Write(ex);
            mini = null;
        }
    });

    private void OnMiniExpand() => ShowWindow();

    private void OnMiniClosed(object sender, WindowEventArgs args)
    {
        if (mini != null)
        {
            mini.Closed -= OnMiniClosed;
            mini.ViewModel.ExpandRequested -= OnMiniExpand;
            mini = null;
        }

        if (!exiting) ShowWindow();
    }

    private void CloseMini()
    {
        if (mini == null) return;
        mini.Closed -= OnMiniClosed;
        mini.ViewModel.ExpandRequested -= OnMiniExpand;
        try
        {
            mini.Close();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }

        mini = null;
    }

    private void ApplyMiniTopmost()
    {
        if (mini?.AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = settings.Current.Ui.MiniModeAlwaysOnTop;
    }

    public void ToggleOverlay()
    {
        if (overlay != null)
        {
            try
            {
                overlay.Close();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }

            overlay = null;
            return;
        }

        try
        {
            overlay = provider.GetRequiredService<GamingOverlayWindow>();
            overlay.Closed += (_, _) => overlay = null;
            ApplyOverlay();
            overlay.Activate();
        }
        catch (Exception ex)
        {
            SetStatus("Nie można otworzyć nakładki: " + ex.Message);
            AppLog.Write(ex);
            overlay = null;
        }
    }

    private void ApplyOverlay()
    {
        if (overlay == null || shell == null) return;
        try
        {
            double scale = settings.Current.Ui.OverlayScalePercent / 100d;
            if (overlay.Content is UIElement content)
            {
                content.Opacity = settings.Current.Ui.OverlayOpacityPercent / 100d;
                content.Scale = new Vector3((float)scale, (float)scale, 1);
            }

            overlay.AppWindow.Resize(new SizeInt32((int)(380 * scale), (int)(64 * scale)));
            var area = DisplayArea.GetFromWindowId(shell.AppWindow.Id, DisplayAreaFallback.Primary)?.WorkArea;
            if (area is not { } work) return;
            string position = settings.Current.Ui.OverlayPosition;
            int width = (int)(380 * scale), height = (int)(64 * scale);
            int x = position.StartsWith("Prawy") ? work.X + work.Width - width - 20 : work.X + 20;
            int y = position.EndsWith("dolny") ? work.Y + work.Height - height - 20 : work.Y + 20;
            overlay.AppWindow.Move(new PointInt32(x, y));
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }
    }

    private void SetStatus(string status) => dispatcher.Post(() =>
    {
        Status = status;
        StatusChanged?.Invoke();
    });

    public void Exit()
    {
        exiting = true;
        shell?.Close();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        exiting = true;
        settings.Changed -= ApplySettings;
        monitor.Updated -= OnMetrics;
        engine.Changed -= OnState;
        voice.Changed -= OnState;
        toasts.Activated -= ShowWindow;
        if (shell != null)
        {
            shell.AppWindow.Closing -= OnClosing;
            shell.AppWindow.Changed -= OnShellChanged;
        }

        hotkeys?.Dispose();
        tray?.Dispose();
        CloseMini();
        try
        {
            overlay?.Close();
        }
        catch
        {
        }
    }
}
