using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Voice;
using SentinelX.WinUI.Core;

namespace SentinelX.WinUI.ViewModels;

public sealed record CommandSuggestion(string Text, string Hint, bool IsHistory);

/// <summary>
/// The command bar: typing, suggestions (catalogue + // shortcuts + session history),
/// live execution status, cancellation and short Sentinel replies.
/// </summary>
public sealed partial class CommandBarViewModel : ObservableObject, IDisposable
{
    private readonly CommandExecutor executor;
    private readonly IVoiceService voice;
    private readonly ConversationMemoryService memory;
    private readonly IUiDispatcher dispatcher;
    private readonly List<string> history = [];
    private int historyIndex = -1;
    private string lastInput = "";
    private bool disposed;

    [ObservableProperty] private string input = "";
    [ObservableProperty] private string statusLine = "Gotowy na polecenie";
    [ObservableProperty] private string replyLine = "";
    [ObservableProperty] private int focusVersion;

    public ObservableCollection<CommandSuggestion> Suggestions { get; } = [];
    public CommandExecutor Executor => executor;

    /// <summary>Raised after a command finishes — the shell refreshes the history panel.</summary>
    public event Action? Executed;

    /// <summary>Raised for // entries that open UI instead of running a command.</summary>
    public event Action<SlashEntry>? SlashNavigationRequested;

    public CommandBarViewModel(CommandExecutor executor, IVoiceService voice,
        ConversationMemoryService memory, IUiDispatcher dispatcher)
    {
        this.executor = executor;
        this.voice = voice;
        this.memory = memory;
        this.dispatcher = dispatcher;
        if (!memory.PrivateMode && memory.GetDraft().Length > 0) Input = memory.GetDraft();
        voice.CommandRecognized += OnVoiceCommand;
        RefreshSuggestions();
    }

    partial void OnInputChanged(string value)
    {
        historyIndex = -1;
        if (!memory.PrivateMode)
        {
            try
            {
                memory.SaveDraft(value);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }
        }

        RefreshSuggestions();
    }

    private void OnVoiceCommand(string command) =>
        dispatcher.Post(() => _ = SendAsync(command, fromVoice: true));

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        string value = Input ?? "";
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            foreach (var entry in SlashCatalog.Filter(value[2..]).Take(8))
                Suggestions.Add(new CommandSuggestion("//" + entry.Trigger, entry.Label, false));
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            foreach (string item in history.TakeLast(8).Reverse())
                Suggestions.Add(new CommandSuggestion(item, "historia", true));
            return;
        }

        foreach (var entry in CommandCatalog.Search(value).Take(8))
            Suggestions.Add(new CommandSuggestion(entry.CommandText ?? entry.Title, entry.Description, false));
        foreach (string item in history.Where(h => h.Contains(value, StringComparison.OrdinalIgnoreCase)).TakeLast(2).Reverse())
            Suggestions.Add(new CommandSuggestion(item, "historia", true));
    }

    [RelayCommand]
    private Task Submit(string? text) => SendAsync(text ?? Input, fromVoice: false);

    [RelayCommand]
    private void Cancel()
    {
        executor.Cancel();
        StatusLine = "Anulowanie zadania…";
    }

    /// <summary>Puts a command into the box without running it — the palette never executes on its own.</summary>
    public void StageCommand(string text)
    {
        Input = text;
        FocusVersion++;
        StatusLine = "Polecenie przygotowane — sprawdź treść i dopiero wtedy wyślij.";
    }

    public void PreviousHistory()
    {
        if (history.Count == 0) return;
        historyIndex = historyIndex < 0 ? history.Count - 1 : Math.Max(0, historyIndex - 1);
        Input = history[historyIndex];
    }

    public void NextHistory()
    {
        if (historyIndex < 0) return;
        historyIndex++;
        Input = historyIndex >= history.Count ? "" : history[historyIndex];
        if (historyIndex >= history.Count) historyIndex = -1;
    }

    private async Task SendAsync(string raw, bool fromVoice)
    {
        string text = raw.Trim();
        if (text.Length == 0) return;

        if (!fromVoice && text.StartsWith("//", StringComparison.Ordinal))
        {
            var entry = SlashCatalog.TryResolve(text[2..].Trim());
            if (entry is { Kind: SlashKind.Command })
            {
                await SendAsync(entry.Target, fromVoice: false).ConfigureAwait(true);
                return;
            }

            if (entry != null)
            {
                Input = "";
                SlashNavigationRequested?.Invoke(entry);
                return;
            }
        }

        string normalized = CommandText.Normalize(text);
        if (!fromVoice && normalized is "ponow" or "ponow odpowiedz" or "ponow to" or "sproboj jeszcze raz")
        {
            if (lastInput.Length == 0)
            {
                StatusLine = "Nie mam czego ponowić — w tej sesji nie wysłano jeszcze polecenia.";
                return;
            }

            text = lastInput;
            StatusLine = "Ponawiam ostatnie polecenie…";
        }

        if (!executor.CanSend(out string reason))
        {
            StatusLine = reason;
            return;
        }

        if (history is not { Count: > 0 } || !string.Equals(history[^1], text, StringComparison.Ordinal))
            history.Add(text);
        while (history.Count > 50) history.RemoveAt(0);
        lastInput = text;

        Input = "";
        ReplyLine = "";
        StatusLine = fromVoice ? "Polecenie głosowe: " + Clip(text, 80) : "Wysyłanie…";

        IntentResult? result;
        try
        {
            result = await executor.ExecuteAsync(text, fromVoice).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            StatusLine = "Błąd: " + ex.Message;
            return;
        }

        if (result == null) return;
        ReplyLine = "Sentinel: " + FirstLine(result.Text, 180);
        StatusLine = result.Action?.Status switch
        {
            ActionStatus.Failed => "Zadanie nie powiodło się — szczegóły w Historii",
            ActionStatus.Cancelled => "Zadanie anulowane",
            ActionStatus.Unverified => "Wynik bez pełnej weryfikacji — sprawdź Historię",
            _ => "Gotowe"
        };
        try
        {
            Executed?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }
    }

    private static string FirstLine(string value, int max)
    {
        string line = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line.Length <= max ? line : line[..max].TrimEnd() + "…";
    }

    private static string Clip(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        voice.CommandRecognized -= OnVoiceCommand;
        try
        {
            memory.FlushDraft();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }
    }
}
