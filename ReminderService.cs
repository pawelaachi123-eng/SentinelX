using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

public sealed record SentinelReminder(string Id, DateTimeOffset DueAt, string Text);

/// <summary>Small local reminder queue with bounded storage; event-driven reminders are not inferred or fabricated.</summary>
public sealed class ReminderService
{
    private const int MaximumReminders = 100;
    private readonly object gate = new();
    private readonly string path;
    private readonly List<SentinelReminder> pending = [];
    private DateTimeOffset nextDeliveryRetry;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ReminderService(string? path = null)
    {
        this.path = path ?? Path.Combine(AppPaths.Root, "reminders.json");
        Load();
    }

    public string? TryProcess(string command)
    {
        string normalized = ConversationMemoryService.Normalize(command).Trim().TrimEnd('.', '!', '?');
        if (normalized is "moje przypomnienia" or "lista przypomnien" or "pokaz przypomnienia") return List();
        Match cancel = Regex.Match(normalized, @"^(?:anuluj|usun) przypomnienie (?<id>[a-f0-9]{10})$", RegexOptions.IgnoreCase);
        if (cancel.Success) return Cancel(cancel.Groups["id"].Value);
        Match relative = Regex.Match(normalized,
            @"^przypomnij(?: mi)? za (?:(?<amount>\d{1,4})\s*)?(?<unit>pol godziny|sekund(?:e|y)?|minut(?:e|y|a)?|godzin(?:e|y|a)?|dni|dzien) (?:o |zeby )?(?<text>.+)$",
            RegexOptions.IgnoreCase);
        if (relative.Success)
        {
            string unit = relative.Groups["unit"].Value;
            int amount = relative.Groups["amount"].Success && int.TryParse(relative.Groups["amount"].Value, out int parsed) ? parsed : 1;
            if (amount < 1) return "Podaj dodatni czas, np. „przypomnij mi za 20 minut o spotkaniu”.";
            TimeSpan delay = unit == "pol godziny" ? TimeSpan.FromMinutes(30)
                : unit.StartsWith("sekund") ? TimeSpan.FromSeconds(amount)
                : unit.StartsWith("minut") ? TimeSpan.FromMinutes(amount)
                : unit.StartsWith("godzin") ? TimeSpan.FromHours(amount)
                : TimeSpan.FromDays(amount);
            if (delay > TimeSpan.FromDays(365)) return "Przypomnienia są ograniczone do jednego roku.";
            return Add(DateTimeOffset.Now + delay, relative.Groups["text"].Value);
        }

        Match tomorrow = Regex.Match(normalized,
            @"^przypomnij(?: mi)? jutro(?: o (?<time>\d{1,2}(?::\d{2})?))? (?:o |zeby )?(?<text>.+)$",
            RegexOptions.IgnoreCase);
        if (tomorrow.Success)
        {
            TimeOnly time = new(9, 0);
            if (tomorrow.Groups["time"].Success)
            {
                string value = tomorrow.Groups["time"].Value;
                bool valid = value.Contains(':')
                    ? TimeOnly.TryParseExact(value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
                    : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int hour) && hour is >= 0 and <= 23;
                if (!valid) return "Nie rozumiem godziny. Użyj np. 18 albo 18:30.";
                if (!value.Contains(':')) time = new TimeOnly(int.Parse(value, CultureInfo.InvariantCulture), 0);
            }
            return Add(new DateTimeOffset(DateTime.Today.AddDays(1).Add(time.ToTimeSpan()), DateTimeOffset.Now.Offset), tomorrow.Groups["text"].Value);
        }
        if (normalized.StartsWith("przypomnij", StringComparison.Ordinal))
            return "Podaj czas i treść, np. „przypomnij mi za 20 minut o pobraniu” albo „przypomnij jutro o 18:30 o zadaniu”.";
        return null;
    }

    public IReadOnlyList<SentinelReminder> TakeDue(DateTimeOffset? now = null)
    {
        lock (gate)
        {
            DateTimeOffset current = now ?? DateTimeOffset.Now;
            var due = pending.Where(x => x.DueAt <= current).OrderBy(x => x.DueAt).ToArray();
            if (due.Length == 0 || current < nextDeliveryRetry) return [];
            var ids = due.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            pending.RemoveAll(x => ids.Contains(x.Id));
            if (!SaveLocked()) { pending.AddRange(due); nextDeliveryRetry = current.AddSeconds(30); return []; }
            nextDeliveryRetry = default;
            return due;
        }
    }

    public string List()
    {
        lock (gate)
        {
            if (pending.Count == 0) return "Nie ma oczekujących przypomnień.";
            return "Oczekujące przypomnienia:\n" + string.Join("\n", pending.OrderBy(x => x.DueAt)
                .Select(x => $"· {x.DueAt.ToLocalTime():dd.MM.yyyy HH:mm} — {x.Text} (id {x.Id})"));
        }
    }

    private string Cancel(string id)
    {
        lock (gate)
        {
            var reminder = pending.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (reminder == null) return "Nie znaleziono oczekującego przypomnienia o takim ID.";
            pending.Remove(reminder);
            if (!SaveLocked()) { pending.Add(reminder); return "Nie udało się zapisać anulowania; przypomnienie pozostaje aktywne."; }
            return "Anulowano przypomnienie: " + reminder.Text;
        }
    }

    private string Add(DateTimeOffset dueAt, string text)
    {
        text = Regex.Replace(text.Trim().Trim('"', '„', '”'), @"\s+", " ");
        if (text.Length is < 2 or > 300) return "Treść przypomnienia musi mieć od 2 do 300 znaków.";
        string normalized = ConversationMemoryService.Normalize(text);
        if (Regex.IsMatch(normalized, @"\b(hasl\w*|password\w*|kod jednorazow\w*|kod weryfikacyjn\w*|otp|pin|token\w*|secret\w*|sekret\w*|api ?key|klucz prywatn\w*|2fa|mfa)\b"))
            return "Nie zapiszę przypomnienia zawierającego prawdopodobne hasło lub kod. Podaj neutralny opis bez sekretu.";
        lock (gate)
        {
            if (pending.Count >= MaximumReminders) return "Osiągnięto limit 100 oczekujących przypomnień.";
            var reminder = new SentinelReminder(Guid.NewGuid().ToString("N")[..10], dueAt, text);
            pending.Add(reminder);
            if (!SaveLocked()) { pending.Remove(reminder); return "Nie udało się trwale zapisać przypomnienia; nie zgłaszam, że zostało ustawione."; }
            return $"Ustawiono przypomnienie na {dueAt.ToLocalTime():dd.MM.yyyy HH:mm}: {text} (id {reminder.Id}).";
        }
    }

    private void Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 1_000_000) return;
            var loaded = JsonSerializer.Deserialize<List<SentinelReminder>>(File.ReadAllText(path)) ?? [];
            pending.AddRange(loaded.Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Text) &&
                x.Text.Length <= 300 && x.DueAt > DateTimeOffset.Now && x.DueAt <= DateTimeOffset.Now.AddYears(1)).Take(MaximumReminders));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
    }

    private bool SaveLocked()
    {
        string temp = path + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) return false;
            Directory.CreateDirectory(directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(pending, JsonOptions));
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { try { File.Delete(temp); } catch { } return false; }
    }
}
