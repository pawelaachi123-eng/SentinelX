using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Automation;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Notifications;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
using SentinelX.Views.Overlay;
using SentinelX.ViewModels;
namespace SentinelX.Services.Desktop;

/// <summary>Owns Windows interop and UI lifetime; no business logic in window code-behind.</summary>
public sealed class DesktopService(ISettingsService settings, IActionEngine engine, IVoiceService voice,
    ISystemMonitorService monitor, IUiDispatcher dispatcher, OverlayViewModel overlayVm,
    Services.Link.AlertFeed alerts, INotificationService notifications, AutomationService automations,
    Views.Link.LinkUi linkUi) : IDesktopService, IDisposable
{
    private Window? window;
    private TrayService? tray;
    private GlobalHotkeyService? hotkeys;
    private OverlayWindow? overlay;
    private bool exiting;
    private bool disposed;
    private bool? startupApplied;
    private readonly StartupService startup = new("Sentinel X");
    private ResourceDictionary? palette;
    private DateTime? highUsageSince;
    private DateTime lastAlert;
    public string Status { get; private set; } = "";
    public event Action? StatusChanged;
    public void Attach(Window target)
    {
        if (window != null) return;
        window = target;
        Services.Link.PhoneHint.Open = linkUi.ShowPhoneWindow;
        window.Closing += Closing;
        window.SourceInitialized += SourceInitialized;
        window.Loaded += Loaded;
        window.StateChanged += WindowStateChanged;
        settings.Changed += ApplySettings;
        monitor.Updated += MetricsUpdated;
        engine.Changed += StateChanged; voice.Changed += StateChanged;
        notifications.Published += NotificationPublished;
        SystemEvents.UserPreferenceChanged += PreferencesChanged;
        ApplySettings();
    }
    private void PreferencesChanged(object sender, UserPreferenceChangedEventArgs e) => dispatcher.Post(ApplySettings);
    private void SourceInitialized(object? sender, EventArgs args)
    {
        if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1") return;
        try
        {
            hotkeys = new GlobalHotkeyService(window!, ShowWindow, engine.EmergencyStop);
            if (!hotkeys.EmergencyRegistered) SetStatus("Ctrl+Shift+X jest zajęty. Użyj przycisku STOP.");
        }
        catch (Exception ex) { SetStatus("Nie udało się zarejestrować skrótów: " + ex.Message); }
    }
    private async void Loaded(object sender, RoutedEventArgs args)
    {
        if (window == null) return;
        window.Loaded -= Loaded;
        if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1") { monitor.Start(); return; }
        try
        {
            tray = new TrayService();
            tray.OpenRequested += ShowWindow;
            tray.PhoneRequested += linkUi.ShowPhoneWindow;
            tray.VoiceOnRequested += StartVoice;
            tray.VoiceOffRequested += voice.Stop;
            tray.EmergencyStopRequested += engine.EmergencyStop;
            tray.ExitRequested += Exit;
            StateChanged();
        }
        catch (Exception ex) { SetStatus("Zasobnik niedostępny; zamknięcie zakończy aplikację. " + ex.Message); }
        monitor.Start();
        automations.Start();
        // Started by Windows (autostart): stay quietly in the tray; the phone and the assistant work without a window.
        if ((settings.Current.Startup.StartMinimized || App.AutostartLaunch) && tray != null) window.Hide();
        if (settings.Current.Startup.StartVoiceOnLaunch)
        {
            try { await voice.StartAsync(settings.Current.Voice.SelectedMicrophoneDevice, false, CancellationToken.None); }
            catch (Exception ex) { SetStatus(ex.Message); }
        }
    }
    private async void StartVoice()
    {
        try { await voice.StartAsync(settings.Current.Voice.SelectedMicrophoneDevice, false, CancellationToken.None); }
        catch (Exception ex) { SetStatus(ex.Message); }
    }
    private void WindowStateChanged(object? sender, EventArgs args)
    { if (window?.WindowState == WindowState.Minimized && settings.Current.Ui.MinimizeToTray && tray != null) window.Hide(); }
    private void Closing(object? sender, CancelEventArgs args)
    {
        if (!exiting && settings.Current.Ui.CloseToTray && tray != null) { args.Cancel = true; window?.Hide(); return; }
        exiting = true; engine.EmergencyStop(); voice.Stop(); overlay?.Close(); Application.Current.Shutdown();
    }
    private void StateChanged() => dispatcher.Post(() => tray?.UpdateState(voice.State != VoiceState.Off, engine.IsStopped, !string.IsNullOrEmpty(monitor.Current.Game)));
    private void NotificationPublished(AppNotification notification) => dispatcher.Post(() =>
    {
        if (!disposed) tray?.ShowInfo(notification.Title, notification.Message);
    });
    private void MetricsUpdated(SystemSnapshot snapshot) => dispatcher.Post(() =>
    {
        StateChanged();
        var watch = settings.Current.Watch;
        double ramPercent = snapshot.RamTotal > 0 ? snapshot.RamUsed / snapshot.RamTotal * 100 : double.NaN;
        bool high = watch.Enabled && (snapshot.Cpu >= watch.CpuAlertPercent || ramPercent >= watch.RamAlertPercent);
        if (!high) highUsageSince = null;
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
                alerts.Add("warn", "Wysokie obciążenie komputera", message + " — utrzymuje się dłużej niż " + watch.MinSecondsBeforeAlert + " s.");
            }
        }
        Application.Current.Resources["SxAnimationsEnabled"] = settings.Current.Ui.AnimationsEnabled && string.IsNullOrEmpty(snapshot.Game);
    });
    private void ApplySettings()
    {
        if (window == null || disposed) return;
        bool light = settings.Current.Ui.Theme == "Light";
        if (settings.Current.Ui.Theme == "System")
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); light = key?.GetValue("AppsUseLightTheme") is int v && v != 0; }
            catch (Exception ex) { AppLog.Write(ex); }
        }
        window.ThemeMode = light ? ThemeMode.Light : ThemeMode.Dark;
        string theme = light ? "LightTheme" : settings.Current.Ui.Theme == "Deep Dark" ? "DeepDarkTheme" : "DarkTheme";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (palette != null) dictionaries.Remove(palette);
        palette = new ResourceDictionary { Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative) };
        dictionaries.Add(palette);
        Application.Current.Resources["SxAccentCyanColor"] = (Color)ColorConverter.ConvertFromString(settings.Current.Ui.AccentColor);
        Application.Current.Resources["SxAnimationsEnabled"] = settings.Current.Ui.AnimationsEnabled && string.IsNullOrEmpty(monitor.Current.Game);
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
    private void ApplyOverlay()
    {
        if (overlay == null) return;
        overlay.Opacity = settings.Current.Ui.OverlayOpacityPercent / 100d;
        overlay.LayoutTransform = new ScaleTransform(settings.Current.Ui.OverlayScalePercent / 100d, settings.Current.Ui.OverlayScalePercent / 100d);
        overlay.UpdateLayout();
        var area = SystemParameters.WorkArea;
        string position = settings.Current.Ui.OverlayPosition;
        overlay.Left = position.StartsWith("Prawy") ? area.Right - overlay.ActualWidth - 20 : area.Left + 20;
        overlay.Top = position.EndsWith("dolny") ? area.Bottom - overlay.ActualHeight - 20 : area.Top + 20;
    }
    public void ShowWindow() => dispatcher.Post(() => { window?.Show(); if (window != null) { window.WindowState = WindowState.Normal; window.Activate(); } });
    public void ToggleOverlay()
    {
        if (overlay != null) { overlay.Close(); overlay = null; return; }
        overlay = new OverlayWindow { DataContext = overlayVm };
        overlay.Closed += (_, _) => overlay = null;
        overlay.Show(); ApplyOverlay();
    }
    private void SetStatus(string status) => dispatcher.Post(() => { Status = status; StatusChanged?.Invoke(); });
    public void Exit() { exiting = true; window?.Close(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; exiting = true;
        settings.Changed -= ApplySettings; monitor.Updated -= MetricsUpdated; engine.Changed -= StateChanged; voice.Changed -= StateChanged;
        notifications.Published -= NotificationPublished;
        SystemEvents.UserPreferenceChanged -= PreferencesChanged;
        if (window != null) { window.Closing -= Closing; window.SourceInitialized -= SourceInitialized; window.Loaded -= Loaded; window.StateChanged -= WindowStateChanged; }
        hotkeys?.Dispose(); tray?.Dispose(); overlay?.Close();
    }
}
