using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX.Services.PhoneBridge;

public sealed record PhoneSkillResult(string Status, string Message, string Evidence = "");
public sealed record PhoneSkillDescriptor(string Name, IReadOnlyList<string> Intents,
    IReadOnlyList<string> Parameters, IReadOnlyList<string> Permissions);

public interface IPhoneSkill
{
    PhoneSkillDescriptor Descriptor { get; }
    Task<PhoneSkillResult> ExecuteAsync(string intent, IReadOnlyDictionary<string, string> parameters, CancellationToken token);
}

/// <summary>Validated in-process skill registry. Skills receive bounded named parameters, never shell/code strings.</summary>
public sealed class PhoneSkillRegistry
{
    private readonly Dictionary<string, IPhoneSkill> byIntent = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public IReadOnlyList<PhoneSkillDescriptor> Descriptors
    {
        get { lock (gate) return byIntent.Values.Distinct().Select(x => x.Descriptor).ToArray(); }
    }

    public void Register(IPhoneSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        PhoneSkillDescriptor descriptor = skill.Descriptor;
        if (string.IsNullOrWhiteSpace(descriptor.Name) || descriptor.Name.Length > 64 || descriptor.Intents.Count is < 1 or > 32 || descriptor.Parameters.Count > 16)
            throw new ArgumentException("Opis skilla przekracza dozwolone limity.", nameof(skill));
        if (descriptor.Intents.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x, @"^[A-Z0-9_]{1,48}$")) ||
            descriptor.Parameters.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x, @"^[a-zA-Z][a-zA-Z0-9_]{0,31}$")))
            throw new ArgumentException("Skill zawiera nieprawidłowy intent lub parametr.", nameof(skill));
        lock (gate)
        {
            if (descriptor.Intents.Any(byIntent.ContainsKey)) throw new InvalidOperationException("Intent jest już zarejestrowany.");
            foreach (string intent in descriptor.Intents) byIntent[intent] = skill;
        }
    }

    public async Task<PhoneSkillResult> ExecuteAsync(string intent, IReadOnlyDictionary<string, string> parameters, CancellationToken token)
    {
        IPhoneSkill? skill;
        lock (gate) byIntent.TryGetValue(intent, out skill);
        if (skill == null) return new("UNSUPPORTED", "Ten intent nie jest zarejestrowany w Sentinel Bridge.");
        if (parameters.Keys.Any(key => !skill.Descriptor.Parameters.Contains(key, StringComparer.Ordinal)) ||
            parameters.Values.Any(value => value == null || value.Length > 512 || value.Any(char.IsControl)))
            return new("FAILED", "Parametry nie pasują do manifestu tego skilla.");
        return await skill.ExecuteAsync(intent, parameters, token).ConfigureAwait(false);
    }
}

/// <summary>Small deterministic bridge skill router. It accepts named intents, never model-generated code.</summary>
public sealed class PhoneSkillExecutor
{
    private static readonly HashSet<string> MacroSafeIntents = new(StringComparer.Ordinal)
    {
        "TV_POWER_ON", "TV_POWER_OFF", "TV_VOLUME_UP", "TV_VOLUME_DOWN", "TV_VOLUME_SET", "TV_MUTE", "TV_HDMI_1", "TV_YOUTUBE", "LIGHT_SET_BRIGHTNESS", "PC_LAUNCH_APP"
    };
    private readonly PhoneSkillRegistry registry = new();
    private readonly string macroPath;
    private readonly DeviceControlTool? devices;
    public IReadOnlyList<PhoneSkillDescriptor> Skills => registry.Descriptors;
    public void Register(IPhoneSkill skill) => registry.Register(skill);

    public PhoneSkillExecutor(SystemMonitor monitor, SentinelX.Services.Apps.IAppLauncherService appLauncher,
        DeviceControlTool? devices = null, DownloadContextService? downloads = null, string? macroPath = null)
    {
        this.devices = devices;
        this.macroPath = macroPath ?? Path.Combine(AppPaths.Root, "PhoneBridge", "macros.json");
        registry.Register(new PcMonitorSkill(monitor, downloads));
        registry.Register(new PcControlSkill(appLauncher));
        registry.Register(new TVControlSkill(devices));
    }

    public async Task<PhoneSkillResult> ExecuteAsync(string intent, IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        if (intent == "PHONE_TEXT" && parameters.TryGetValue("text", out string? text))
            return await TryRunMacroAsync(text, cancellationToken).ConfigureAwait(false)
                ?? new PhoneSkillResult("UNSUPPORTED", "Nie znaleziono wcześniej zdefiniowanego makra dla tej frazy. Żadnej akcji nie wykonano.");
        return await registry.ExecuteAsync(intent, parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PhoneSkillResult?> TryRunMacroAsync(string text, CancellationToken token)
    {
        string normalized = ConversationMemoryService.Normalize(text).Trim().TrimEnd('.', '!', '?');
        if (normalized.Length is < 2 or > 80 || !File.Exists(macroPath)) return null;
        try
        {
            using var stream = new FileStream(macroPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > 32 * 1024) return new("FAILED", "Plik makr przekracza limit 32 KiB; nie uruchomiono makra.");
            using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            char[] buffer = new char[4096];
            var jsonBuilder = new System.Text.StringBuilder(Math.Min((int)stream.Length, 32 * 1024));
            while (true)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                if (read == 0) break;
                if (jsonBuilder.Length > 32 * 1024 - read)
                    return new("FAILED", "Plik makr przekracza limit 32 KiB; nie uruchomiono makra.");
                jsonBuilder.Append(buffer, 0, read);
            }
            using JsonDocument document = JsonDocument.Parse(jsonBuilder.ToString(), new JsonDocumentOptions { MaxDepth = 8 });
            if (!document.RootElement.TryGetProperty("macros", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 20)
                return new("FAILED", "Konfiguracja makr jest nieprawidłowa lub przekracza limit 20 makr.");
            var matches = new List<JsonElement>();
            foreach (JsonElement macro in entries.EnumerateArray())
            {
                if (!macro.TryGetProperty("phrases", out JsonElement phrases) || phrases.ValueKind != JsonValueKind.Array || phrases.GetArrayLength() is < 1 or > 8) continue;
                if (phrases.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() is { Length: >= 2 and <= 80 } &&
                    ConversationMemoryService.Normalize(x.GetString() ?? "").Trim().TrimEnd('.', '!', '?') == normalized)) matches.Add(macro);
            }
            if (matches.Count == 0) return null;
            if (matches.Count != 1) return new("FAILED", "Fraza pasuje do więcej niż jednego makra. Niczego nie wykonano.");
            JsonElement selected = matches[0];
            string name = selected.TryGetProperty("name", out JsonElement nameNode) && nameNode.ValueKind == JsonValueKind.String ? nameNode.GetString() ?? "" : "";
            if (name.Length is < 2 or > 40 || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[\p{L}\p{N} _-]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
                !selected.TryGetProperty("steps", out JsonElement steps) ||
                steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() is < 1 or > 5)
                return new("FAILED", "Makro ma nieprawidłową nazwę lub liczbę kroków; niczego nie wykonano.");
            var validated = new List<(string Intent, Dictionary<string, string> Parameters)>();
            foreach (JsonElement step in steps.EnumerateArray())
            {
                if (step.ValueKind != JsonValueKind.Object || !step.TryGetProperty("intent", out JsonElement intentNode) || intentNode.ValueKind != JsonValueKind.String)
                    return new("FAILED", "Makro zawiera nieprawidłowy krok; niczego nie wykonano.");
                string stepIntent = intentNode.GetString() ?? "";
                if (!MacroSafeIntents.Contains(stepIntent)) return new("FAILED", "Makro może wywoływać tylko zatwierdzone akcje o niskim ryzyku na sparowanych światłach/TV.");
                var args = new Dictionary<string, string>(StringComparer.Ordinal);
                if (step.TryGetProperty("parameters", out JsonElement argsNode))
                {
                    if (argsNode.ValueKind != JsonValueKind.Object) return new("FAILED", "Parametry kroku makra są nieprawidłowe.");
                    foreach (JsonProperty property in argsNode.EnumerateObject())
                    {
                        if (property.Name is not ("target" or "value") || property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() is not { } arg ||
                            arg.Length > 48 || arg.Any(c => !(char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '%')))
                            return new("FAILED", "Parametr makra nie jest dozwolony; niczego nie wykonano.");
                        args[property.Name] = arg;
                    }
                }
                if (!IsMacroStepValid(stepIntent, args, out string validationError))
                    return new("FAILED", $"Makro „{name}” nie spełnia walidacji wszystkich kroków: {validationError} Niczego nie wykonano.");
                validated.Add((stepIntent, args));
            }
            var evidence = new List<string>();
            for (int i = 0; i < validated.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var step = validated[i];
                PhoneSkillResult result = await registry.ExecuteAsync(step.Intent, step.Parameters, token).ConfigureAwait(false);
                if (result.Status != "SUCCESS")
                {
                    string partial = i == 0 ? "Nie wykonano żadnego kroku." : $"Wykonano i zweryfikowano {i} z {validated.Count} kroków; nie cofnięto wcześniejszych zmian.";
                    return new(i == 0 ? result.Status : "PARTIAL", $"Makro „{name}” zatrzymano. {partial} {result.Message}", string.Join(" · ", evidence));
                }
                evidence.Add(result.Message);
            }
            return new("SUCCESS", $"Makro „{name}” wykonano: {validated.Count} kroków; wszystkie zwróciły wynik zweryfikowany.", string.Join(" · ", evidence));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { return new("FAILED", "Nie udało się bezpiecznie odczytać makr. Żadnej akcji nie wykonano."); }
    }

    private bool IsMacroStepValid(string intent, IReadOnlyDictionary<string, string> args, out string error)
    {
        error = "";
        if (args.Keys.Any(key => key is not ("target" or "value"))) { error = "nieznany parametr."; return false; }
        if (intent == "PC_LAUNCH_APP")
        {
            if (args.Count != 1 || !args.TryGetValue("target", out string? app) || !AppLauncherService.IsKnownLaunchTarget(app))
            { error = "aplikacja nie należy do allowlisty Sentinel."; return false; }
            return true;
        }

        if (intent == "LIGHT_SET_BRIGHTNESS" || intent == "TV_VOLUME_SET")
        {
            if (!args.TryGetValue("value", out string? rawValue) || !int.TryParse(rawValue, out int value) || value is < 0 or > 100)
            { error = "wartość musi wynosić od 0 do 100."; return false; }
        }
        else if (args.ContainsKey("value")) { error = "ten krok nie przyjmuje wartości."; return false; }

        string alias = args.TryGetValue("target", out string? target) ? target : "tv";
        if (alias.Length == 0 || devices == null) { error = "brak sparowanego celu Home Assistant."; return false; }
        PairedDevice? paired = devices.GetPairedDevices().FirstOrDefault(x => x.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));
        if (paired == null) { error = "alias celu nie jest sparowany."; return false; }
        string[] allowedDomains = intent switch
        {
            "LIGHT_SET_BRIGHTNESS" => ["light"],
            "TV_POWER_ON" or "TV_POWER_OFF" => ["media_player", "remote"],
            _ => ["media_player"]
        };
        if (!allowedDomains.Contains(paired.Domain, StringComparer.Ordinal)) { error = "typ sparowanego celu nie obsługuje tej akcji."; return false; }
        return true;
    }
}

public sealed class PcMonitorSkill(SystemMonitor monitor, DownloadContextService? downloads) : IPhoneSkill
{
    public PhoneSkillDescriptor Descriptor { get; } = new("PCMonitorSkill",
        ["PC_STATUS", "PC_GPU_TEMP", "PC_DOWNLOAD_STATUS"], [], ["read-system-status"]);

    public Task<PhoneSkillResult> ExecuteAsync(string intent, IReadOnlyDictionary<string, string> parameters, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (intent == "PC_GPU_TEMP") return Task.FromResult(new PhoneSkillResult("UNSUPPORTED", "Temperatura GPU nie jest dostępna: ten build nie ma wiarygodnego czujnika temperatury. Nie zgaduję wartości."));
        if (intent == "PC_DOWNLOAD_STATUS")
        {
            RecentDownload? recent = downloads?.GetMostRecent();
            if (recent == null) return Task.FromResult(downloads?.IsAvailable == true
                ? new PhoneSkillResult("SUCCESS", "Nie wykryto ostatnio zakończonego pobrania.")
                : new PhoneSkillResult("UNVERIFIED", "Monitor pobrań jest niedostępny; nie mam bieżącego wyniku."));
            return Task.FromResult(new PhoneSkillResult("SUCCESS", $"Ostatni stabilny plik w Pobranych: {System.IO.Path.GetFileName(recent.Path)} · {recent.CompletedAt:HH:mm}", "Nie otwarto ani nie przesłano pliku."));
        }
        float cpu = monitor.GetCpuUsage();
        double used = monitor.GetUsedRamGB(), total = monitor.GetTotalRamGB(), ramPercent = monitor.GetRamUsagePercent();
        float gpu = monitor.GetGpuUsagePercent();
        string cpuText = float.IsFinite(cpu) ? $"{cpu:0.0}%" : "niedostępny";
        string ramText = double.IsFinite(used) && double.IsFinite(total) && double.IsFinite(ramPercent)
            ? $"{used:0.0}/{total:0.0} GiB ({ramPercent:0.0}%)" : "niedostępny";
        string gpuText = float.IsFinite(gpu) ? $"{gpu:0.0}% użycia" : "użycie niedostępne";
        return Task.FromResult(new PhoneSkillResult("SUCCESS", $"PC: ONLINE · CPU {cpuText} · RAM {ramText} · GPU {gpuText}", "Odczyt lokalnych liczników systemowych."));
    }
}

public sealed class PcControlSkill(SentinelX.Services.Apps.IAppLauncherService appLauncher) : IPhoneSkill
{
    public PhoneSkillDescriptor Descriptor { get; } = new("PCSkill",
        ["PC_LAUNCH_APP", "PC_LOCK", "PC_SCREENSHOT", "PC_SEND_FILE", "PC_CLOSE_APP", "PC_SHUTDOWN", "PC_RESTART"],
        ["target"], ["launch-allowlist", "local-approval-for-sensitive-actions"]);

    public async Task<PhoneSkillResult> ExecuteAsync(string intent, IReadOnlyDictionary<string, string> parameters, CancellationToken token)
    {
        string value = parameters.TryGetValue("target", out string? target) ? target.Trim() : "";
        switch (intent)
        {
            case "PC_LAUNCH_APP":
                if (value.Length is < 1 or > 64 || !AppLauncherService.IsKnownLaunchTarget(value))
                    return new("FAILED", "Aplikacja nie znajduje się na dozwolonej liście zdalnego uruchamiania. Niczego nie uruchomiłem.");
                ActionExecutionResult launched = await appLauncher.LaunchAsync(value, token).ConfigureAwait(false);
                return new(launched.Verified ? "SUCCESS" : launched.Success ? "UNVERIFIED" : "FAILED", launched.Message, launched.Evidence);
            case "PC_LOCK":
                if (!OperatingSystem.IsWindows()) return new("UNSUPPORTED", "Blokowanie stacji roboczej jest dostępne tylko w Windows.");
                bool accepted = LockWorkStation();
                return accepted ? new("UNVERIFIED", "Windows przyjął żądanie zablokowania stacji. Stan ekranu blokady nie jest potwierdzony.")
                    : new("FAILED", "Windows odrzucił żądanie zablokowania stacji.");
            case "PC_SCREENSHOT": return new("UNSUPPORTED", "Zdalny screenshot nie jest jeszcze dostępny. Nie przechwyciłem ani nie przesłałem ekranu.");
            case "PC_SEND_FILE": return new("UNSUPPORTED", "Transfer plików między PC a telefonem nie jest jeszcze dostępny.");
            case "PC_CLOSE_APP": return new("UNSUPPORTED", "Zdalne zamykanie wymaga lokalnego potwierdzenia; ta wersja nie obsługuje go, aplikacji nie zamknąłem.");
            case "PC_SHUTDOWN":
            case "PC_RESTART": return new("UNSUPPORTED", "Wyłączenie i restart wymagają lokalnego potwierdzenia; nie wykonałem tej operacji.");
            default: return new("UNSUPPORTED", "To polecenie PC nie jest obsługiwane.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}

public sealed class TVControlSkill(DeviceControlTool? devices) : IPhoneSkill
{
    public PhoneSkillDescriptor Descriptor { get; } = new("TVControlSkill",
        ["TV_POWER_ON", "TV_POWER_OFF", "TV_VOLUME_UP", "TV_VOLUME_DOWN", "TV_VOLUME_SET", "TV_MUTE", "TV_HDMI_1", "TV_YOUTUBE", "TV_NEXT_CHANNEL", "LIGHT_SET_BRIGHTNESS"],
        ["target", "value"], ["paired-device-control"]);

    public async Task<PhoneSkillResult> ExecuteAsync(string intent, IReadOnlyDictionary<string, string> parameters, CancellationToken token)
    {
        if (intent == "TV_NEXT_CHANNEL") return new("UNSUPPORTED", "Zmiana kanału nie jest dostępna w obecnym adapterze Home Assistant.");
        if (devices == null) return new("UNSUPPORTED", "Sterowanie TV wymaga jawnie skonfigurowanej i sparowanej integracji Home Assistant.");
        string alias = parameters.TryGetValue("target", out string? value) ? value.Trim() : "tv";
        if (alias.Length == 0 && intent != "LIGHT_SET_BRIGHTNESS") alias = "tv";
        if (alias.Length > 48 || alias.Any(char.IsControl) || intent == "LIGHT_SET_BRIGHTNESS" && alias.Length == 0)
            return new("FAILED", "Nieprawidłowy alias urządzenia.");
        string level = parameters.TryGetValue("value", out string? requestedLevel) ? requestedLevel.Trim().TrimEnd('%') : "";
        if ((intent is "TV_VOLUME_SET" or "LIGHT_SET_BRIGHTNESS") && (!int.TryParse(level, out int numericLevel) || numericLevel is < 0 or > 100))
            return new("FAILED", "Podaj wartość od 0 do 100.");
        string command = intent switch
        {
            "TV_POWER_ON" => $"wlacz {alias}",
            "TV_POWER_OFF" => $"wylacz {alias}",
            "TV_MUTE" => $"wycisz {alias}",
            "TV_VOLUME_UP" => $"daj glosniej {alias}",
            "TV_VOLUME_DOWN" => $"daj ciszej {alias}",
            "TV_VOLUME_SET" => $"glosnosc {alias} na {level}%",
            "LIGHT_SET_BRIGHTNESS" => $"jasnosc {alias} na {level}%",
            "TV_HDMI_1" => $"przelacz {alias} na HDMI 1",
            "TV_YOUTUBE" => $"otworz youtube na {alias}",
            _ => ""
        };
        string? result = await devices.TryProcessAsync(command, token).ConfigureAwait(false);
        if (result == null) return new("UNSUPPORTED", "Polecenie nie pasuje do obsługiwanej akcji sparowanego urządzenia.");
        if (result.StartsWith("Zweryfikowano:", StringComparison.Ordinal))
            return new("SUCCESS", result, "Home Assistant zwrócił zgodny stan urządzenia.");
        bool refusedWithoutDispatch = result.Contains("Nie wysłałem", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("niczego nie wysłałem", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("zablokował", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("niczego nie przełączyłem", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("Nie mam jednoznacznie", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("nie jest skonfigurowane", StringComparison.OrdinalIgnoreCase);
        return new(refusedWithoutDispatch ? "FAILED" : "UNVERIFIED", result);
    }
}
