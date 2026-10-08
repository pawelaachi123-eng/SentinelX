using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Voice;

namespace SentinelX.WinUI.Core;

/// <summary>
/// Single command-execution path shared by the main command bar and Mini Mode.
/// Feeds the live phase + streamed answer to the UI, reports the outcome to the
/// <see cref="ICoreStateService"/> and speaks short answers when voice is on.
/// </summary>
public sealed partial class CommandExecutor : ObservableObject, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IVoiceService voice;
    private readonly ICoreStateService core;
    private readonly IUiDispatcher dispatcher;
    private readonly StringBuilder stream = new();
    private readonly object streamGate = new();
    private bool disposed;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isStopped;
    [ObservableProperty] private string phase = "";
    [ObservableProperty] private bool isStreaming;
    [ObservableProperty] private string streamingText = "";

    public CommandExecutor(IActionEngine engine, IVoiceService voice, ICoreStateService core, IUiDispatcher dispatcher)
    {
        this.engine = engine;
        this.voice = voice;
        this.core = core;
        this.dispatcher = dispatcher;
        engine.Changed += Sync;
        Sync();
    }

    private void Sync() => dispatcher.Post(() =>
    {
        if (disposed) return;
        IsBusy = engine.IsBusy;
        IsStopped = engine.IsStopped;
        Phase = engine.CurrentAction?.Phase ?? "";
        HasPendingPermission = engine.HasPendingPermission;
        PermissionSummary = engine.PermissionSummary;
    });

    public bool CanSend(out string reason)
    {
        if (engine.IsStopped)
        {
            reason = "STOP jest aktywny. Wznów Sentinel przed wysłaniem.";
            return false;
        }

        if (engine.IsBusy)
        {
            reason = "Trwa zadanie — poczekaj albo je anuluj.";
            return false;
        }

        reason = "";
        return true;
    }

    public async Task<IntentResult?> ExecuteAsync(string input, bool fromVoice = false)
    {
        lock (streamGate) stream.Clear();
        dispatcher.Post(() => { IsStreaming = false; StreamingText = ""; });

        IntentResult result;
        try
        {
            result = await engine.ExecuteAsync(input, fromVoice: fromVoice, onDelta: OnChunk).ConfigureAwait(false);
        }
        finally
        {
            dispatcher.Post(() => { IsStreaming = false; });
        }

        core.ReportResult(result.Action);
        if (fromVoice)
        {
            try
            {
                voice.Speak(result.Text);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }
        }

        return result;
    }

    private void OnChunk(string chunk)
    {
        lock (streamGate) stream.Append(chunk);
        dispatcher.Post(() =>
        {
            if (disposed) return;
            string text;
            lock (streamGate) text = stream.ToString();
            StreamingText = text.Length > 600 ? text[^600..] : text;
            IsStreaming = true;
        });
    }

    public void Cancel() => engine.Cancel();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        engine.Changed -= Sync;
    }
}
