using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using SentinelX.WinUI.Core;

namespace SentinelX.WinUI.UI.Nexus;

/// <summary>
/// Event bridge between the native Sentinel services and the Nexus web UI (WebView2).
/// Native → web: {type:"core.state", data:{state, detail, task, audio}}.
/// Web → native: {type:"command.submit", id, data:{text, fromVoice}} → {type:"host.result"|"host.error", id, data}.
/// </summary>
public sealed class NexusBridge : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly WebView2 _view;
    private readonly ICoreStateService _core;
    private readonly CommandExecutor _executor;
    private readonly DispatcherQueue _queue;
    private DispatcherQueueTimer? _audioTimer;

    public NexusBridge(WebView2 view, ICoreStateService core, CommandExecutor executor, DispatcherQueue queue)
    {
        _view = view;
        _core = core;
        _executor = executor;
        _queue = queue;
    }

    public async Task AttachAsync()
    {
        await _view.EnsureCoreWebView2Async();
        var wv = _view.CoreWebView2;
        wv.Settings.AreDefaultContextMenusEnabled = false;
        wv.Settings.IsStatusBarEnabled = false;
        wv.WebMessageReceived += OnMessage;
        wv.PermissionRequested += (_, e) =>
        {
            // Microphone is governed by Sentinel's own Voice settings inside the UI.
            if (e.PermissionKind == CoreWebView2PermissionKind.Microphone) e.State = CoreWebView2PermissionState.Allow;
        };
        wv.NavigationCompleted += (_, _) => PushState(new CoreStateChanged(_core.State, _core.Detail, _core.ActiveTask));
        _core.Changed += OnCoreChanged;

        // Native mic energy (when native voice pipeline owns the microphone).
        _audioTimer = _queue.CreateTimer();
        _audioTimer.Interval = TimeSpan.FromMilliseconds(50);
        _audioTimer.Tick += (_, _) => { if (_core.AudioLevel > 0.001) Post(new { type = "host.audio", data = new { level = _core.AudioLevel } }); };
        _audioTimer.Start();
    }

    public void NotifyVisibility(bool visible) => Post(new { type = "host.visibility", data = new { visible } });

    private void OnCoreChanged(CoreStateChanged e) => _queue.TryEnqueue(() => PushState(e));

    private void PushState(CoreStateChanged e) =>
        Post(new { type = "core.state", data = new { state = e.State.ToString().ToLowerInvariant(), detail = e.Detail, task = e.ActiveTask } });

    private void Post(object message)
    {
        if (_view.CoreWebView2 is null) return;
        _view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    private async void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string? id = null;
        try
        {
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var data = root.TryGetProperty("data", out var d) ? d : default;

            switch (type)
            {
                case "host.hello":
                    Post(new { type = "host.info", id, data = new { host = "winui", state = _core.State.ToString().ToLowerInvariant() } });
                    break;
                case "command.submit":
                    var text = data.GetProperty("text").GetString() ?? string.Empty;
                    var fromVoice = data.TryGetProperty("fromVoice", out var fv) && fv.GetBoolean();
                    if (!_executor.CanSend(out var reason))
                    {
                        Post(new { type = "host.error", id, data = new { error = reason } });
                        return;
                    }
                    var result = await _executor.ExecuteAsync(text, fromVoice).ConfigureAwait(true);
                    Post(new { type = "host.result", id, data = new { text = result?.Text ?? string.Empty } });
                    break;
                case "command.cancel":
                    _executor.Cancel();
                    break;
            }
        }
        catch (Exception ex)
        {
            Post(new { type = "host.error", id, data = new { error = ex.Message } });
        }
    }

    public void Dispose()
    {
        _core.Changed -= OnCoreChanged;
        _audioTimer?.Stop();
        if (_view.CoreWebView2 is not null) _view.CoreWebView2.WebMessageReceived -= OnMessage;
    }
}
