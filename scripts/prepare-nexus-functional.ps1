# Extend Nexus host with explicit native API routes and native voice ownership.
# Run on the GitHub Windows runner AFTER prepare-nexus.ps1 copied the saved UI.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$target = Join-Path $repo 'SentinelX.WinUI/UI/Nexus/NexusBridge.cs'
$window = Join-Path $repo 'SentinelX.WinUI/UI/Nexus/NexusWindow.cs'
$encoding = [Text.UTF8Encoding]::new($false)
function Replace-Exactly([string]$s, [string]$old, [string]$new) {
    $n = ([regex]::Matches($s, [regex]::Escape($old))).Count
    if ($n -ne 1) { throw "Native Nexus source drift: expected 1 match, found $n : $old" }
    return $s.Replace($old, $new)
}
$b = [IO.File]::ReadAllText($target)
$b = Replace-Exactly $b 'using SentinelX.WinUI.Core;' @'
using SentinelX.WinUI.Core;
using SentinelX.Services.Voice;
using System.Linq;
using SentinelX;
'@
$b = Replace-Exactly $b '    private readonly DispatcherQueue _queue;' @'
    private readonly DispatcherQueue _queue;
    private readonly TaskService _tasks;
    private readonly ActionTaskRegistry _actions;
    private readonly IVoiceService _voice;
'@
$b = Replace-Exactly $b 'public NexusBridge(WebView2 view, ICoreStateService core, CommandExecutor executor, DispatcherQueue queue)' 'public NexusBridge(WebView2 view, ICoreStateService core, CommandExecutor executor, DispatcherQueue queue, TaskService tasks, ActionTaskRegistry actions, IVoiceService voice)'
$b = Replace-Exactly $b '        _queue = queue;' @'
        _queue = queue;
        _tasks = tasks;
        _actions = actions;
        _voice = voice;
'@
$b = Replace-Exactly $b '        _core.Changed += OnCoreChanged;' @'
        _core.Changed += OnCoreChanged;
        _voice.CommandRecognized += OnNativeVoiceCommand;
'@
$b = Replace-Exactly $b '        _core.Changed -= OnCoreChanged;' @'
        _core.Changed -= OnCoreChanged;
        _voice.CommandRecognized -= OnNativeVoiceCommand;
'@
# IMPORTANT: The native voice service executes only once, with fromVoice=true;
# browser mic is disabled in host mode. No cloud transcription, no duplicate commands.
$impl = @'
    private void OnNativeVoiceCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _queue.TryEnqueue(async () =>
        {
            string reply = "";
            string? error = null;
            try
            {
                if (!_executor.CanSend(out var reason)) error = reason;
                else reply = (await _executor.ExecuteAsync(text, true))?.Text ?? "";
            }
            catch (Exception ex) { error = ex.Message; }
            Post(new { type = "host.voice.result", data = new { text, reply, error } });
        });
    }

    // This is an intentionally small native facade over the ACTUAL legacy services.
    // Unsupported ESP32/device routes must fail: never infer physical connectivity,
    // mint device credentials, or treat an UI entry as authorized hardware.
    private object HandleApi(string method, string path)
    {
        if (method == "GET" && path == "/tasks")
        {
            var stored = _tasks.GetTasks(includeDone: true).Select(t => (object)new
            {
                id = t.Id, title = t.Title, kind = "saved.task",
                status = t.Status == TaskRecord.StatusDone ? "done" :
                         t.Status == TaskRecord.StatusDoing ? "running" : "queued",
                progress = t.Status == TaskRecord.StatusDone ? 1d : 0d,
                created_at = t.CreatedAt.ToUniversalTime().ToString("O"),
                error = (string?)null
            });
            var current = _actions.GetTasks().Select(t => (object)new
            {
                id = t.Id, title = t.Description, kind = "pc.action",
                status = t.FinishedAt == null ? "running" :
                         t.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                         t.Status.Equals("SUCCEEDED", StringComparison.OrdinalIgnoreCase) ? "done" :
                         t.Status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) ? "cancelled" : "error",
                progress = t.TotalSteps > 0 ? (double)t.CompletedSteps / t.TotalSteps : 0d,
                created_at = t.StartedAt.ToString("O"),
                error = t.FinishedAt != null && t.Status != "COMPLETED" && t.Status != "SUCCEEDED" ? t.Status : null
            });
            return stored.Concat(current).ToArray();
        }
        if (method == "DELETE" && path == "/tasks")
        {
            var completed = _tasks.GetTasks(includeDone: true)
                .Where(t => t.Status == TaskRecord.StatusDone).ToArray();
            int removed = completed.Count(t => _tasks.DeleteTask(t.Id));
            return new { deleted = removed };
        }
        if (method == "GET" && path == "/diagnostics")
            return new { backend = "native-winui", database = "local", host = "winui3",
                         tasks_total = _tasks.GetTasks(includeDone: true).Count,
                         running_actions = _actions.GetTasks().Count(t => t.FinishedAt == null),
                         esp32 = "not_exposed_in_nexus_host" };
        throw new NotSupportedException($"Nexus native API unavailable: {method} {path}. Feature requires explicit native integration.");
    }

'@
$b = Replace-Exactly $b '    private void OnCoreChanged(CoreStateChanged e)' ($impl + '    private void OnCoreChanged(CoreStateChanged e)')
$b = Replace-Exactly $b '                case "command.cancel":' @'
                case "host.api.request":
                    var method = data.TryGetProperty("method", out var me) ? me.GetString() ?? "GET" : "GET";
                    var path = data.GetProperty("path").GetString() ?? "";
                    Post(new { type = "host.result", id, data = HandleApi(method, path) });
                    break;
                case "command.cancel":
'@
[IO.File]::WriteAllText($target, $b, $encoding)

$w = [IO.File]::ReadAllText($window)
$w = Replace-Exactly $w 'using SentinelX.WinUI.Core;' @'
using SentinelX.WinUI.Core;
using SentinelX.Services.Voice;
using SentinelX;
'@
$w = Replace-Exactly $w 'public NexusWindow(ICoreStateService core, CommandExecutor executor)' 'public NexusWindow(ICoreStateService core, CommandExecutor executor, TaskService tasks, ActionTaskRegistry actions, IVoiceService voice)'
$w = Replace-Exactly $w 'new NexusBridge(_view, core, executor, DispatcherQueue)' 'new NexusBridge(_view, core, executor, DispatcherQueue, tasks, actions, voice)'
[IO.File]::WriteAllText($window, $w, $encoding)
Write-Host 'Native Nexus: real local tasks, action queue and microphone bridge installed.' -ForegroundColor Green
