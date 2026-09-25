using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Desktop;

namespace SentinelX.ViewModels;

/// <summary>Tryb współpracy ekranowej — live podgląd całego pulpitu (wszystkie monitory), drugi kursor
/// w innym kolorze (niebieski), pełna kontrola myszki po włączeniu przyciskiem w UI (tylko przycisk,
/// bez aktywacji głosem, aby nie włączyć przypadkiem). Wyraźny wskaźnik (czerwona ramka) i STOP
/// awaryjny (Ctrl+Shift+X) wyłączają tryb.</summary>
public sealed partial class CollaborationViewModel : ObservableObject
{
    private readonly IScreenCaptureService screen;
    private readonly ICoPilotCursorService cursor;
    private readonly ISettingsService settings;
    private readonly IUiDispatcher dispatcher;
    private readonly ActionHistoryService history;

    [ObservableProperty] private BitmapSource? liveFrame;
    [ObservableProperty] private string status = "Gotowy — tryb współpracy wyłączony. Włącz przyciskiem, aby AI widziało ekran i mogło sterować myszką.";
    [ObservableProperty] private bool isLive;
    [ObservableProperty] private bool isCollaborationActive;
    [ObservableProperty] private int selectedMonitorIndex;
    [ObservableProperty] private string cursorStatus = "Drugi kursor: wyłączony";
    [ObservableProperty] private string monitorsInfo = "";

    public IReadOnlyList<MonitorInfo> Monitors => screen.Monitors;

    public CollaborationViewModel(IScreenCaptureService screen, ICoPilotCursorService cursor, ISettingsService settings, IUiDispatcher dispatcher, ActionHistoryService history)
    {
        this.screen = screen;
        this.cursor = cursor;
        this.settings = settings;
        this.dispatcher = dispatcher;
        this.history = history;
        screen.FrameCaptured += OnFrame;
        cursor.StateChanged += OnCursorState;
        cursor.PositionChanged += OnCursorPos;
        RefreshMonitorsInfo();
    }

    private void RefreshMonitorsInfo()
    {
        var list = Monitors;
        MonitorsInfo = $"Wykryte monitory: {list.Count}\n" + string.Join("\n", list.Select(m => $"· {m.Index}: {m.Name} {m.Width}x{m.Height} @({m.X},{m.Y}) {(m.Primary ? "PRIMARY" : "")}"));
        SelectedMonitorIndex = 0;
    }

    private void OnFrame(BitmapSource bmp)
    {
        dispatcher.Post(() => LiveFrame = bmp);
    }

    private void OnCursorState()
    {
        dispatcher.Post(() =>
        {
            IsCollaborationActive = cursor.IsActive;
            CursorStatus = cursor.Status;
            Status = cursor.IsActive
                ? "● TRYB WSPÓŁPRACY AKTYWNY — ekran widoczny LIVE, drugi kursor (niebieski) steruje myszką. Wyłącz przyciskiem lub Ctrl+Shift+X."
                : "Gotowy — tryb współpracy wyłączony.";
        });
    }

    private void OnCursorPos(System.Windows.Point p)
    {
        // Could update UI with position if needed
    }

    [RelayCommand]
    private void ToggleCollaboration()
    {
        if (cursor.IsActive)
        {
            DisableCollaboration();
        }
        else
        {
            EnableCollaboration();
        }
    }

    [RelayCommand]
    private void EnableCollaboration()
    {
        try
        {
            string id = history.CreateActionId();
            history.AddRunning(id, "COLLAB_ENABLE", "włącz współpracę ekranową");
            cursor.Enable();
            int fps = settings.Current.Collaboration.LiveFps;
            screen.StartLive(fps);
            IsLive = true;
            IsCollaborationActive = true;
            Status = "● TRYB WSPÓŁPRACY AKTYWNY — podgląd LIVE wszystkich monitorów, drugi kursor (niebieski) widoczny. Możesz przełączać monitory, AI widzi wszystko. Wyłącz przyciskiem lub Ctrl+Shift+X.";
            history.AddResult(id, "COLLAB_ENABLE", "włącz współpracę ekranową",
                ActionExecutionResult.VerifiedSuccess(Status, $"Monitory: {Monitors.Count}; FPS: {fps}; drugi kursor: {settings.Current.Collaboration.CursorColor}"));
        }
        catch (Exception ex)
        {
            Status = "Nie udało się włączyć współpracy: " + ex.Message;
        }
    }

    [RelayCommand]
    private void DisableCollaboration()
    {
        try
        {
            string id = history.CreateActionId();
            history.AddRunning(id, "COLLAB_DISABLE", "wyłącz współpracę ekranową");
            screen.StopLive();
            cursor.Disable();
            IsLive = false;
            IsCollaborationActive = false;
            Status = "Tryb współpracy wyłączony — ekran nie jest już widoczny, kontrola myszki wyłączona.";
            history.AddResult(id, "COLLAB_DISABLE", "wyłącz współpracę ekranową",
                ActionExecutionResult.VerifiedSuccess(Status, $"Wyłączono {DateTime.Now:O}"));
        }
        catch (Exception ex)
        {
            Status = "Błąd wyłączania: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CaptureAllAsync()
    {
        try
        {
            Status = "Przechwytywanie całego pulpitu...";
            var bmp = await screen.CaptureAllAsync();
            if (bmp != null)
            {
                LiveFrame = bmp;
                Status = $"Zrzut całego pulpitu ({bmp.PixelWidth}x{bmp.PixelHeight}) — {DateTime.Now:HH:mm:ss}";
                if (settings.Current.Collaboration.SaveCaptures)
                {
                    string? path = await screen.CaptureAndSaveAsync(null);
                    if (path != null) Status += $"\nZapisano lokalnie: {path}";
                }
            }
            else Status = "Nie udało się przechwycić ekranu.";
        }
        catch (Exception ex) { Status = "Błąd zrzutu: " + ex.Message; }
    }

    [RelayCommand]
    private async Task CaptureMonitorAsync()
    {
        try
        {
            int idx = SelectedMonitorIndex;
            if (idx < 0 || idx >= Monitors.Count) idx = 0;
            Status = $"Przechwytywanie monitora {idx}...";
            var bmp = await screen.CaptureMonitorAsync(idx);
            if (bmp != null)
            {
                LiveFrame = bmp;
                Status = $"Monitor {idx}: {bmp.PixelWidth}x{bmp.PixelHeight} — {DateTime.Now:HH:mm:ss}";
            }
            else Status = "Nie udało się przechwycić monitora.";
        }
        catch (Exception ex) { Status = "Błąd: " + ex.Message; }
    }

    [RelayCommand]
    private void MoveCursorToCenter()
    {
        if (!cursor.IsActive) { Status = "Włącz najpierw tryb współpracy przyciskiem."; return; }
        try
        {
            int x = (int)(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth / 2);
            int y = (int)(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight / 2);
            cursor.MoveTo(x, y);
            Status = $"Drugi kursor przeniesiony na środek: {x},{y}";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void Click()
    {
        if (!cursor.IsActive) { Status = "Włącz najpierw tryb współpracy."; return; }
        cursor.Click();
        Status = $"Klik w {cursor.Position.X:0},{cursor.Position.Y:0}";
    }

    [RelayCommand]
    private void RightClick()
    {
        if (!cursor.IsActive) { Status = "Włącz najpierw tryb współpracy."; return; }
        cursor.RightClick();
        Status = $"Prawy klik w {cursor.Position.X:0},{cursor.Position.Y:0}";
    }

    public void OnEmergencyStop()
    {
        if (IsCollaborationActive)
        {
            DisableCollaboration();
            Status = "AWARYJNY STOP — tryb współpracy wyłączony.";
        }
        if (IsLive) { screen.StopLive(); IsLive = false; }
    }
}
