using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.History;

namespace SentinelX.WinUI.ViewModels;

/// <summary>
/// Expandable history of recent actions and Sentinel replies at the bottom of the shell.
/// </summary>
public sealed partial class HistoryPanelViewModel : ObservableObject, IDisposable
{
    private readonly IHistoryService history;
    private readonly IActionEngine engine;
    private readonly IUiDispatcher dispatcher;
    private bool disposed;

    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private ActionRecord? liveAction;

    public ObservableCollection<ActionHistoryEntry> Actions { get; } = [];
    public ObservableCollection<ConversationMemoryEntry> Replies { get; } = [];

    public HistoryPanelViewModel(IHistoryService history, IActionEngine engine, IUiDispatcher dispatcher)
    {
        this.history = history;
        this.engine = engine;
        this.dispatcher = dispatcher;
        engine.Changed += SyncLive;
        SyncLive();
        Refresh();
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    public void Refresh() => dispatcher.Post(() =>
    {
        if (disposed) return;
        try
        {
            Actions.Clear();
            foreach (var entry in history.ReadActions().TakeLast(12).Reverse())
                Actions.Add(entry);
            Replies.Clear();
            foreach (var entry in history.ReadConversation()
                         .Where(e => !string.Equals(e.Role, "user", StringComparison.OrdinalIgnoreCase))
                         .TakeLast(6).Reverse())
                Replies.Add(entry);
            Error = history.StorageError ?? "";
        }
        catch (Exception ex)
        {
            Error = "Nie można odczytać historii: " + ex.Message;
            AppLog.Write(ex);
        }
    });

    private void SyncLive() => dispatcher.Post(() =>
    {
        if (!disposed) LiveAction = engine.CurrentAction;
    });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        engine.Changed -= SyncLive;
    }
}
