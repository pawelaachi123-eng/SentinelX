using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

public sealed record AutomationRuleSnapshot(string Id, string Trigger, string Condition, string Action, string Message, DateTimeOffset? LastTriggeredAt);

/// <summary>
/// Bounded local event-condition-action rules. Triggers are limited to measured CPU/RAM/GPU
/// thresholds and stable-download events; the only action is a local notification.
/// </summary>
public sealed class AutomationRuleService : IDisposable
{
    private const int MaximumRules = 32;
    private const int MaximumJsonBytes = 64 * 1024;
    private readonly object gate = new();
    private readonly List<StoredRule> rules = [];
    private readonly Dictionary<string, DateTimeOffset> aboveSince = new(StringComparer.Ordinal);
    private readonly HashSet<string> firedEpisodes = new(StringComparer.Ordinal);
    private readonly string path;
    private readonly Func<bool> persistenceAllowed;
    private readonly PerformanceHistoryService performance;
    private readonly DownloadContextService downloads;
    private bool disposed;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public event Action<string>? NoticeRaised;
    public string? LastStorageError { get; private set; }

    public AutomationRuleService(PerformanceHistoryService performance, DownloadContextService downloads,
        string? path = null, Func<bool>? persistenceAllowedProvider = null)
    {
        this.path = path ?? Path.Combine(AppPaths.Root, "Automations", "rules.json");
        this.performance = performance;
        this.downloads = downloads;
        persistenceAllowed = persistenceAllowedProvider ?? (() => true);
        if (CanPersist()) Load();
        performance.Sampled += ObservePerformance;
        downloads.Completed += ObserveDownload;
    }

    public string? TryProcess(string command)
    {
        string query = ConversationMemoryService.Normalize(command).Trim().TrimEnd('.', '!', '?', ',');
        if (query is "automatyzacje" or "lista automatyzacji" or "pokaz automatyzacje") return FormatRules();
        Match remove = Regex.Match(query, @"^(?:usun|wylacz) automatyzacje (?<id>a-[a-f0-9]{8})$", RegexOptions.CultureInvariant);
        if (remove.Success) return Remove(remove.Groups["id"].Value);

        if (query == "powiadom mnie gdy pobieranie sie zakonczy") return AddDownloadRule("");
        // Preserve the existing one-shot download watcher phrase; persistent rules are distinct.
        if (query == "powiadom mnie gdy pobieranie sie skonczy" || query == "powiadom mnie po zakonczeniu pobierania") return null;
        Match download = Regex.Match(query,
            @"^powiadom mnie gdy pobieranie o nazwie zawierajacej (?<name>[^,]{2,60}) sie zakonczy$",
            RegexOptions.CultureInvariant);
        if (download.Success) return AddDownloadRule(download.Groups["name"].Value.Trim());

        Match threshold = Regex.Match(query,
            @"^powiadom mnie gdy (?<metric>cpu|ram|gpu) (?:przekroczy|przekracza|jest powyzej) (?<limit>\d{1,3})\s*%? przez (?<seconds>\d{1,3}) sekund(?:y|e)?$",
            RegexOptions.CultureInvariant);
        if (threshold.Success && int.TryParse(threshold.Groups["limit"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int limit) &&
            int.TryParse(threshold.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
            return AddPerformanceRule(threshold.Groups["metric"].Value, limit, seconds);
        if (query.StartsWith("powiadom mnie gdy ", StringComparison.Ordinal))
            return "Obsługiwane reguły: „powiadom mnie gdy CPU przekroczy 90% przez 30 sekund” albo „powiadom mnie gdy pobieranie się zakończy”. Reguły tylko powiadamiają — nie wykonują zmian systemowych.";
        return null;
    }

    public IReadOnlyList<AutomationRuleSnapshot> GetRules()
    {
        lock (gate) return rules.Select(Snapshot).ToArray();
    }

    /// <summary>Public for deterministic regression tests; production calls it from the bounded sampler.</summary>
    public void ObservePerformance(PerformanceSample sample)
    {
        List<string> notices = [];
        lock (gate)
        {
            if (disposed) return;
            foreach (StoredRule rule in rules.Where(x => x.Enabled && x.Trigger == "PERFORMANCE"))
            {
                double value = rule.Metric switch
                {
                    "CPU" => sample.CpuPercent,
                    "RAM" => sample.RamPercent,
                    "GPU" => sample.GpuPercent,
                    _ => double.NaN
                };
                if (!double.IsFinite(value) || value < rule.Threshold)
                {
                    aboveSince.Remove(rule.Id);
                    firedEpisodes.Remove(rule.Id);
                    continue;
                }
                if (!aboveSince.TryGetValue(rule.Id, out DateTimeOffset since))
                {
                    aboveSince[rule.Id] = sample.CapturedAt;
                    since = sample.CapturedAt;
                }
                bool durationMet = sample.CapturedAt - since >= TimeSpan.FromSeconds(rule.DurationSeconds);
                bool cooldownMet = !rule.LastTriggeredAt.HasValue ||
                    sample.CapturedAt - rule.LastTriggeredAt.Value >= TimeSpan.FromMinutes(rule.CooldownMinutes);
                if (!durationMet || !cooldownMet || firedEpisodes.Contains(rule.Id)) continue;
                firedEpisodes.Add(rule.Id);
                rule.LastTriggeredAt = sample.CapturedAt;
                notices.Add($"Automatyzacja {rule.Id}: {rule.Message} ({rule.Metric} {value:0.#}%).");
            }
            if (notices.Count > 0) SaveLocked();
        }
        RaiseNoticeBatch(notices);
    }

    /// <summary>Public for deterministic regression tests; production subscribes to stable download events.</summary>
    public void ObserveDownload(RecentDownload download)
    {
        List<string> notices = [];
        string fileName = SensitiveDataRedactor.Redact(Path.GetFileName(download.Path));
        lock (gate)
        {
            if (disposed) return;
            foreach (StoredRule rule in rules.Where(x => x.Enabled && x.Trigger == "DOWNLOAD"))
            {
                if (rule.Filter.Length > 0 && !fileName.Contains(rule.Filter, StringComparison.OrdinalIgnoreCase)) continue;
                if (rule.LastTriggeredAt is { } last && download.CompletedAt - last < TimeSpan.FromMinutes(rule.CooldownMinutes)) continue;
                rule.LastTriggeredAt = download.CompletedAt;
                notices.Add($"Automatyzacja {rule.Id}: {rule.Message} — {fileName}.");
            }
            if (notices.Count > 0) SaveLocked();
        }
        RaiseNoticeBatch(notices);
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        performance.Sampled -= ObservePerformance;
        downloads.Completed -= ObserveDownload;
        NoticeRaised = null;
    }

    private string AddPerformanceRule(string metric, int threshold, int seconds)
    {
        metric = metric.ToUpperInvariant();
        int maximum = metric == "GPU" ? 100 : 100;
        if (threshold is < 1 || threshold > maximum || seconds is < 1 or > 600)
            return "Zakres reguły: próg 1–100%, czas utrzymania 1–600 sekund.";
        string message = $"{metric} przekracza {threshold}% przez {seconds} s";
        return Add(new StoredRule
        {
            Id = "A-" + Guid.NewGuid().ToString("N")[..8], Trigger = "PERFORMANCE", Metric = metric,
            Threshold = threshold, DurationSeconds = seconds, CooldownMinutes = 5, Message = message
        });
    }

    private string AddDownloadRule(string filter)
    {
        filter = SensitiveDataRedactor.Redact(filter.Trim());
        if (filter.Length > 60) return "Filtr nazwy pliku może mieć maksymalnie 60 znaków.";
        return Add(new StoredRule
        {
            Id = "A-" + Guid.NewGuid().ToString("N")[..8], Trigger = "DOWNLOAD", Filter = filter,
            CooldownMinutes = 1, Message = filter.Length == 0 ? "Wykryto stabilne pobranie" : "Zakończono pasujące pobranie"
        });
    }

    private string Add(StoredRule rule)
    {
        lock (gate)
        {
            if (disposed) return "Reguły automatyzacji są wyłączone.";
            if (rules.Count >= MaximumRules) return "Osiągnięto limit 32 reguł automatyzacji.";
            if (Regex.IsMatch(ConversationMemoryService.Normalize(rule.Message), @"\b(hasl\w*|password\w*|kod jednorazow\w*|otp|pin|token\w*|api ?key|klucz prywatn\w*|2fa|mfa)\b"))
                return "Nie zapiszę reguły zawierającej prawdopodobny sekret.";
            rules.Add(rule);
            SaveLocked();
            string persistence = CanPersist() ? (LastStorageError == null ? "zapisana lokalnie" : "zapis niepotwierdzony: " + LastStorageError) : "aktywna tylko do zamknięcia Sentinel (tryb prywatny)";
            return $"Utworzono bezpieczną regułę {rule.Id}: {rule.Message}. Akcja: lokalne powiadomienie; nie wykonuje zmian. Reguła jest {persistence}.";
        }
    }

    private string Remove(string id)
    {
        lock (gate)
        {
            StoredRule? rule = rules.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (rule == null) return "Nie znaleziono aktywnej reguły o takim ID.";
            rules.Remove(rule); aboveSince.Remove(rule.Id); firedEpisodes.Remove(rule.Id); SaveLocked();
            return "Usunięto regułę automatyzacji " + rule.Id + ".";
        }
    }

    private string FormatRules()
    {
        lock (gate)
        {
            if (rules.Count == 0) return "Brak reguł automatyzacji. Mogę obserwować utrzymujący się próg CPU/RAM/GPU albo stabilne pobranie; jedyną akcją jest lokalne powiadomienie.";
            return "Reguły automatyzacji (" + rules.Count + "/" + MaximumRules + "):\n" + string.Join("\n", rules.Select(x =>
                $"· {x.Id} [{(x.Enabled ? "aktywna" : "wyłączona")}] {Describe(x)} — akcja: powiadomienie")) +
                "\nUsuń: „usuń automatyzację " + rules[0].Id + "”.";
        }
    }

    private static string Describe(StoredRule rule) => rule.Trigger == "DOWNLOAD"
        ? (rule.Filter.Length == 0 ? "po stabilnym pobraniu" : "po pobraniu zawierającym „" + rule.Filter + "”")
        : $"gdy {rule.Metric} ≥{rule.Threshold}% przez {rule.DurationSeconds} s";

    private void Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumJsonBytes) return;
            var loaded = JsonSerializer.Deserialize<List<StoredRule>>(File.ReadAllText(path), JsonOptions) ?? [];
            foreach (StoredRule rule in loaded.Take(MaximumRules))
            {
                if (rule == null || !Regex.IsMatch(rule.Id ?? "", @"^A-[a-f0-9]{8}$", RegexOptions.IgnoreCase) ||
                    rule.Trigger is not ("PERFORMANCE" or "DOWNLOAD") || rule.Message is null || rule.Message.Length is < 2 or > 120 ||
                    rule.CooldownMinutes is < 1 or > 240) continue;
                if (rule.Trigger == "PERFORMANCE" && (rule.Metric is not ("CPU" or "RAM" or "GPU") || rule.Threshold is < 1 or > 100 || rule.DurationSeconds is < 1 or > 600)) continue;
                rule.Filter = rule.Filter is { Length: <= 60 } ? rule.Filter : "";
                rules.Add(rule);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { LastStorageError = ex.Message; }
    }

    private void SaveLocked()
    {
        if (!CanPersist()) return;
        string temp = path + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) throw new IOException("Nieprawidłowa ścieżka reguł.");
            Directory.CreateDirectory(directory);
            string json = JsonSerializer.Serialize(rules, JsonOptions);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes) throw new IOException("Reguły przekroczyły limit zapisu.");
            File.WriteAllText(temp, json);
            File.Move(temp, path, true);
            LastStorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { try { File.Delete(temp); } catch { } LastStorageError = ex.Message; }
    }

    private bool CanPersist()
    {
        try { return persistenceAllowed(); }
        catch (Exception ex) { LastStorageError = "Nie można sprawdzić ustawień prywatności: " + ex.Message; return false; }
    }

    private static AutomationRuleSnapshot Snapshot(StoredRule rule) => new(rule.Id, rule.Trigger, Describe(rule), "NOTIFY", rule.Message, rule.LastTriggeredAt);

    private void RaiseNoticeBatch(IReadOnlyList<string> notices)
    {
        if (notices.Count == 0) return;
        string summary = string.Join("\n", notices.Take(3));
        if (notices.Count > 3) summary += "\n+" + (notices.Count - 3) + " innych zgodnych reguł.";
        try { NoticeRaised?.Invoke(summary); }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private sealed class StoredRule
    {
        public string Id { get; set; } = "";
        public string Trigger { get; set; } = "";
        public string Metric { get; set; } = "";
        public int Threshold { get; set; }
        public int DurationSeconds { get; set; }
        public int CooldownMinutes { get; set; } = 5;
        public string Filter { get; set; } = "";
        public string Message { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public DateTimeOffset? LastTriggeredAt { get; set; }
    }
}
