using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Settings;
using SentinelX.WinUI.Core;

namespace SentinelX.WinUI.ViewModels;

/// <summary>
/// Mini Mode: status line, CPU/RAM glimpse and one command box. Shares the
/// <see cref="CommandExecutor"/> with the main shell — one engine, one queue.
/// </summary>
public sealed partial class MiniModeViewModel : ObservableObject
{
    private readonly CommandExecutor executor;
    private readonly ISettingsService settings;

    [ObservableProperty] private string input = "";
    [ObservableProperty] private string statusLine = "";
    [ObservableProperty] private string replyLine = "";
    [ObservableProperty] private bool alwaysOnTop = true;

    public ObservableCollection<string> Suggestions { get; } = [];

    public CommandExecutor Executor => executor;
    public TelemetryViewModel Telemetry { get; }
    public ICoreStateService Core { get; }

    public event Action? ExpandRequested;

    public MiniModeViewModel(CommandExecutor executor, TelemetryViewModel telemetry,
        ICoreStateService core, ISettingsService settings)
    {
        this.executor = executor;
        Telemetry = telemetry;
        Core = core;
        this.settings = settings;
        AlwaysOnTop = settings.Current.Ui.MiniModeAlwaysOnTop;
    }

    partial void OnInputChanged(string value) => RefreshSuggestions();

    partial void OnAlwaysOnTopChanged(bool value)
    {
        settings.Current.Ui.MiniModeAlwaysOnTop = value;
        settings.Save();
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        if (string.IsNullOrWhiteSpace(Input)) return;
        foreach (var entry in CommandCatalog.Search(Input).Take(6))
            Suggestions.Add(entry.CommandText ?? entry.Title);
    }

    [RelayCommand]
    private async Task Submit(string? text)
    {
        string command = (text ?? Input).Trim();
        if (command.Length == 0) return;
        if (!executor.CanSend(out string reason))
        {
            StatusLine = reason;
            return;
        }

        Input = "";
        ReplyLine = "";
        StatusLine = "Wysyłanie…";
        IntentResult? result;
        try
        {
            result = await executor.ExecuteAsync(command).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusLine = "Błąd: " + ex.Message;
            SentinelX.AppLog.Write(ex);
            return;
        }

        if (result == null) return;
        ReplyLine = "Sentinel: " + FirstLine(result.Text, 120);
        StatusLine = result.Action?.Status switch
        {
            ActionStatus.Failed => "Nie powiodło się",
            ActionStatus.Cancelled => "Anulowano",
            _ => "Gotowe"
        };
    }

    [RelayCommand]
    private void Cancel() => executor.Cancel();

    [RelayCommand]
    private void Expand() => ExpandRequested?.Invoke();

    private static string FirstLine(string value, int max)
    {
        string line = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line.Length <= max ? line : line[..max].TrimEnd() + "…";
    }
}
