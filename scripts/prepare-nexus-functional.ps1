# Integrate the real local task queue, native voice and authenticated SX4 Base into the Nexus host.
# Runs in CI after prepare-nexus.ps1 copies the saved UI. No HTTP fallback or fake device status.
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
using SentinelX.Services.Base;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
using System.Linq;
using System.Threading;
using SentinelX;
'@
$b = Replace-Exactly $b '    private readonly DispatcherQueue _queue;' @'
    private readonly DispatcherQueue _queue;
    private readonly TaskService _tasks;
    private readonly ActionTaskRegistry _actions;
    private readonly IVoiceService _voice;
    private readonly IBaseControl _base;
    private readonly ISettingsService _settings;
    private readonly SpeechOutputService _speech;
'@
$b = Replace-Exactly $b 'public NexusBridge(WebView2 view, ICoreStateService core, CommandExecutor executor, DispatcherQueue queue)' 'public NexusBridge(WebView2 view, ICoreStateService core, CommandExecutor executor, DispatcherQueue queue, TaskService tasks, ActionTaskRegistry actions, IVoiceService voice, IBaseControl baseControl, ISettingsService settings, SpeechOutputService speech)'
$b = Replace-Exactly $b '        _queue = queue;' @'
        _queue = queue;
        _tasks = tasks;
        _actions = actions;
        _voice = voice;
        _base = baseControl;
        _settings = settings;
        _speech = speech;
'@
$b = Replace-Exactly $b '        _core.Changed += OnCoreChanged;' @'
        _core.Changed += OnCoreChanged;
        _voice.CommandRecognized += OnNativeVoiceCommand;
        _voice.Changed += OnVoiceChanged;
        _voice.MetricsUpdated += OnVoiceMetricsUpdated;
        _base.Changed += OnBaseChanged;
        _base.Start();
        if (_settings.Current.Startup.StartVoiceOnLaunch && _voice.State.ToString().Equals("Off", StringComparison.OrdinalIgnoreCase))
            _ = StartVoiceOnLaunchAsync();
'@
$b = Replace-Exactly $b '    private void OnCoreChanged(CoreStateChanged e)' @'
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

    private void OnVoiceChanged() => _queue.TryEnqueue(() => Post(new { type = "host.voice.status", data = BuildVoiceStatus() }));
    private void OnBaseChanged() => _queue.TryEnqueue(() => Post(new { type = "host.base.status", data = BuildBaseStatus() }));
    private void OnVoiceMetricsUpdated(VoiceMetrics metrics) => _queue.TryEnqueue(() => Post(new
    {
        type = "host.voice.metrics",
        data = new { state = metrics.State.ToString().ToLowerInvariant(), raw = metrics.Raw, enhanced = metrics.Enhanced,
            noise = metrics.Noise, snr = metrics.Snr, gain = metrics.Gain, speechDetected = metrics.SpeechDetected, transcript = metrics.Transcript }
    }));

    private async Task StartVoiceOnLaunchAsync()
    {
        try
        {
            var microphones = _voice.GetMicrophones();
            int device = _settings.Current.Voice.SelectedMicrophoneDevice;
            if (device < 0 || device >= microphones.Count || microphones[device].Contains("[DŹWIĘK SYSTEMOWY]", StringComparison.OrdinalIgnoreCase))
                device = RecommendedMicrophone(microphones);
            if (device < 0)
            {
                Post(new { type = "host.voice.error", data = new { error = "Nie znaleziono mikrofonu wejściowego. Podłącz lub włącz mikrofon w Windows." } });
                return;
            }
            _settings.Current.Voice.SelectedMicrophoneDevice = device;
            _settings.Current.Voice.SelectedMicrophoneName = MicrophoneLabel(microphones[device]);
            _settings.Save();
            await _voice.StartAsync(device, false, CancellationToken.None).ConfigureAwait(true);
            if (_voice.State.ToString().Equals("Off", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(_voice.Status);
        }
        catch (Exception ex)
        {
            Post(new { type = "host.voice.error", data = new { error = ex.Message } });
        }
    }

    private static int RecommendedMicrophone(IReadOnlyList<string> microphones)
    {
        for (int i = 0; i < microphones.Count; i++)
            if (!microphones[i].Contains("[DŹWIĘK SYSTEMOWY]", StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
    private static string MicrophoneLabel(string value)
    {
        int separator = value.IndexOf(':');
        return separator >= 0 ? value[(separator + 1)..].Trim() : value;
    }
    private object BuildVoiceStatus()
    {
        var names = _voice.GetMicrophones();
        var microphones = names.Select((name, index) => new
        {
            index, name, loopback = name.Contains("[DŹWIĘK SYSTEMOWY]", StringComparison.OrdinalIgnoreCase)
        }).ToArray();
        string state = _voice.State.ToString().ToLowerInvariant();
        return new
        {
            state, status = _voice.Status, listening = state != "off", hasModels = _voice.HasLocalModels,
            microphones, selectedDevice = _settings.Current.Voice.SelectedMicrophoneDevice,
            selectedName = _settings.Current.Voice.SelectedMicrophoneName,
            speakResponses = _settings.Current.Voice.SpeakResponses,
            speechVoice = _settings.Current.Voice.SpeechVoice,
            speechRate = _settings.Current.Voice.SpeechRate,
            speechVolume = _settings.Current.Voice.SpeechVolume,
            speechVoices = _speech.Voices, speechAvailable = _speech.Available, speechStatus = _speech.Status
        };
    }
    private object BuildBaseStatus()
    {
        var cfg = _base.PublicConfiguration;
        string status = _base.Status;
        return new
        {
            online = status.StartsWith("Base połączona", StringComparison.OrdinalIgnoreCase),
            configured = cfg != null, status,
            host = cfg?.Host, port = cfg?.Port, fingerprint = cfg?.Fingerprint,
            deviceId = cfg?.DeviceId, baseId = cfg?.BaseId, mode = cfg?.Mode ?? "lan",
            grants = cfg?.Grants ?? Array.Empty<string>(), capabilities = _base.Capabilities.ToArray()
        };
    }
    private static string RequiredString(JsonElement body, string key)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException("Brak wymaganego pola: " + key);
        return value.GetString()!;
    }
    private static int RequiredInt(JsonElement body, string key)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(key, out var value) || !value.TryGetInt32(out int number))
            throw new InvalidOperationException("Brak poprawnej liczby w polu: " + key);
        return number;
    }
    private static string OptionalString(JsonElement body, string key, string fallback) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;
    private static string[] ReadGrants(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("grants", out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").ToArray();
    }

    private async Task<object> HandleApiAsync(string method, string path, JsonElement body)
    {
        if (method == "GET" && path == "/voice/status") return BuildVoiceStatus();
        if (method == "POST" && path == "/voice/start")
        {
            int chosen = -1;
            bool explicitDevice = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("device", out var requested) && requested.TryGetInt32(out chosen);
            int device = explicitDevice ? chosen : _settings.Current.Voice.SelectedMicrophoneDevice;
            bool download = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("downloadModels", out var downloadValue) &&
                downloadValue.ValueKind == JsonValueKind.True;
            var microphones = _voice.GetMicrophones();
            if (!explicitDevice && (device < 0 || device >= microphones.Count ||
                microphones[device].Contains("[DŹWIĘK SYSTEMOWY]", StringComparison.OrdinalIgnoreCase)))
                device = RecommendedMicrophone(microphones);
            if (device < 0 || device >= microphones.Count)
                throw new InvalidOperationException("Nie znaleziono fizycznego mikrofonu wejściowego.");
            if (microphones[device].Contains("[DŹWIĘK SYSTEMOWY]", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("To wejście przechwytuje dźwięk systemowy. Wybierz prawdziwy mikrofon.");
            _settings.Current.Voice.SelectedMicrophoneDevice = device;
            _settings.Current.Voice.SelectedMicrophoneName = MicrophoneLabel(microphones[device]);
            _settings.Save();
            await _voice.StartAsync(device, download, CancellationToken.None).ConfigureAwait(true);
            if (_voice.State.ToString().Equals("Off", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(_voice.Status);
            _settings.Current.Startup.StartVoiceOnLaunch = true;
            _settings.Save();
            return BuildVoiceStatus();
        }
        if (method == "POST" && path == "/voice/stop")
        {
            _voice.Stop();
            _settings.Current.Startup.StartVoiceOnLaunch = false;
            _settings.Save();
            return BuildVoiceStatus();
        }
        if (method == "POST" && path == "/voice/calibrate")
        {
            _voice.Calibrate();
            return BuildVoiceStatus();
        }
        if (method == "PUT" && path == "/voice/preferences")
        {
            if (body.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Brak ustawień głosu.");
            var voiceSettings = _settings.Current.Voice;
            if (body.TryGetProperty("speakResponses", out var speak) && (speak.ValueKind == JsonValueKind.True || speak.ValueKind == JsonValueKind.False))
                voiceSettings.SpeakResponses = speak.GetBoolean();
            if (body.TryGetProperty("speechVoice", out var voice) && voice.ValueKind == JsonValueKind.String)
                voiceSettings.SpeechVoice = voice.GetString() ?? "";
            if (body.TryGetProperty("speechRate", out var rate) && rate.TryGetInt32(out int speechRate))
                voiceSettings.SpeechRate = Math.Clamp(speechRate, -5, 5);
            if (body.TryGetProperty("speechVolume", out var volume) && volume.TryGetInt32(out int speechVolume))
                voiceSettings.SpeechVolume = Math.Clamp(speechVolume, 0, 100);
            if (body.TryGetProperty("sensitivity", out var sensitivity) && sensitivity.TryGetDouble(out double sens))
            {
                sens = Math.Clamp(sens, 0.3, 2.5);
                voiceSettings.MicGainTargetRms = Math.Clamp(0.075 / sens, 0.02, 0.20);
                voiceSettings.VadThreshold = Math.Clamp(0.50 - ((sens - 1.0) * 0.12), 0.20, 0.85);
            }
            _settings.Save();
            return BuildVoiceStatus();
        }
        if (method == "POST" && path == "/voice/speak")
        {
            string text = RequiredString(body, "text");
            if (!_speech.Available) throw new InvalidOperationException(_speech.Status);
            _speech.Speak(text, _settings.Current.Voice.SpeechVoice, _settings.Current.Voice.SpeechRate, _settings.Current.Voice.SpeechVolume);
            return BuildVoiceStatus();
        }
        if (method == "PUT" && path == "/voice/selection")
        {
            int device = RequiredInt(body, "device");
            var microphones = _voice.GetMicrophones();
            if (device < 0 || device >= microphones.Count || microphones[device].Contains("[DŹWIĘK SYSTEMOWY]", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Wybierz prawdziwy mikrofon wejściowy.");
            bool wasListening = !_voice.State.ToString().Equals("Off", StringComparison.OrdinalIgnoreCase);
            _settings.Current.Voice.SelectedMicrophoneDevice = device;
            _settings.Current.Voice.SelectedMicrophoneName = MicrophoneLabel(microphones[device]);
            _settings.Save();
            if (wasListening) await _voice.StartAsync(device, false, CancellationToken.None).ConfigureAwait(true);
            return BuildVoiceStatus();
        }

        if (method == "GET" && path == "/base/status") return BuildBaseStatus();
        if (method == "POST" && path == "/base/discover")
        {
            var found = await BaseDiscovery.DiscoverAsync(CancellationToken.None).ConfigureAwait(true);
            return found;
        }
        if (method == "POST" && path == "/base/pair")
        {
            var identity = new BaseIdentity(
                RequiredString(body, "host"),
                RequiredInt(body, "port"),
                RequiredString(body, "fingerprint"),
                checked((ushort)RequiredInt(body, "deviceId")),
                checked((ushort)RequiredInt(body, "baseId")),
                RequiredString(body, "secretHex"),
                OptionalString(body, "mode", "lan"),
                ReadGrants(body));
            await _base.PairAsync(identity, CancellationToken.None).ConfigureAwait(true);
            _base.Start();
            return BuildBaseStatus();
        }
        if (method == "POST" && path == "/base/unpair")
        {
            await _base.UnpairAsync().ConfigureAwait(true);
            return BuildBaseStatus();
        }
        if (method == "POST" && path == "/base/reconnect")
        {
            _base.Start();
            return BuildBaseStatus();
        }
        if (method == "GET" && path == "/base/tasks") return await _base.TasksAsync().ConfigureAwait(true);

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
            var completed = _tasks.GetTasks(includeDone: true).Where(t => t.Status == TaskRecord.StatusDone).ToArray();
            int removed = completed.Count(t => _tasks.DeleteTask(t.Id));
            return new { deleted = removed };
        }
        if (method == "GET" && path == "/diagnostics")
            return new { backend = "native-winui", database = "local", host = "winui3",
                tasks_total = _tasks.GetTasks(includeDone: true).Count,
                running_actions = _actions.GetTasks().Count(t => t.FinishedAt == null),
                esp32 = BuildBaseStatus() };
        throw new NotSupportedException($"Nexus native API unavailable: {method} {path}. Feature requires explicit native integration.");
    }

'@
$b = Replace-Exactly $b '    private void OnCoreChanged(CoreStateChanged e)' ($impl + '    private void OnCoreChanged(CoreStateChanged e)')
$b = Replace-Exactly $b '        _core.Changed -= OnCoreChanged;' @'
        _core.Changed -= OnCoreChanged;
        _voice.CommandRecognized -= OnNativeVoiceCommand;
        _voice.Changed -= OnVoiceChanged;
        _voice.MetricsUpdated -= OnVoiceMetricsUpdated;
        _base.Changed -= OnBaseChanged;
'@
# Inject the native API request handler into the original bridge.
$b = Replace-Exactly $b '                case "command.cancel":' @'
                case "host.api.request":
                    var method = data.TryGetProperty("method", out var me) ? me.GetString() ?? "GET" : "GET";
                    var path = data.GetProperty("path").GetString() ?? "";
                    var body = data.TryGetProperty("body", out var bodyElement) ? bodyElement : default;
                    var apiResult = await HandleApiAsync(method, path, body).ConfigureAwait(true);
                    Post(new { type = "host.result", id, data = apiResult });
                    break;
                case "command.cancel":
'@
[IO.File]::WriteAllText($target, $b, $encoding)

$w = [IO.File]::ReadAllText($window)
$w = Replace-Exactly $w 'using SentinelX.WinUI.Core;' @'
using SentinelX.WinUI.Core;
using SentinelX.Services.Base;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
using SentinelX;
'@
$w = Replace-Exactly $w 'public NexusWindow(ICoreStateService core, CommandExecutor executor)' 'public NexusWindow(ICoreStateService core, CommandExecutor executor, TaskService tasks, ActionTaskRegistry actions, IVoiceService voice, IBaseControl baseControl, ISettingsService settings, SpeechOutputService speech)'
$w = Replace-Exactly $w 'new NexusBridge(_view, core, executor, DispatcherQueue)' 'new NexusBridge(_view, core, executor, DispatcherQueue, tasks, actions, voice, baseControl, settings, speech)'
[IO.File]::WriteAllText($window, $w, $encoding)
Write-Host 'Native Nexus: SX4 pairing/status, microphone selection/autostart, real tasks and voice bridge installed.' -ForegroundColor Green
