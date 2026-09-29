using System.Text;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX.Services.PhoneCall;

/// <summary>
/// Ordinary toolbox endpoint for phone-call requests. The current desktop product deliberately has no
/// cellular-call transport: Windows cannot make an Android SIM call and expose both sides of its audio
/// to this process. A future companion/provider must implement IPhoneCallProvider; absence of one is a
/// hard failure, never a simulated call.
/// </summary>
public sealed class PhoneCallTool
{
    private static readonly Regex CallIntent = new(
        @"^(?:hej )?(?:zadzwon|wykonaj polaczenie|zarezerwuj|zrob rezerwacje|umow)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private readonly IPhoneCallProvider? provider;
    private readonly PhoneCallTranscriptStore history;

    public PhoneCallTool(IPhoneCallProvider? provider = null, PhoneCallTranscriptStore? history = null)
    {
        this.provider = provider;
        this.history = history ?? new PhoneCallTranscriptStore();
    }

    /// <returns>null when the input is not a phone-call tool command.</returns>
    public async Task<string?> TryProcessAsync(string command, CancellationToken cancellationToken = default)
    {
        string raw = (command ?? string.Empty).Trim();
        string normalized = ConversationMemoryService.Normalize(raw);
        string slashCommand = normalized.TrimStart('/').Trim();

        if (slashCommand is "historia rozmow" or "rozmowy historia")
            return await history.FormatRecentAsync(cancellationToken);

        bool isCallRequest;
        try { isCallRequest = CallIntent.IsMatch(normalized); }
        catch (RegexMatchTimeoutException) { isCallRequest = false; }
        if (!isCallRequest) return null;

        cancellationToken.ThrowIfCancellationRequested();
        bool providerAvailable = false;
        try { providerAvailable = provider?.IsAvailable == true; }
        catch { /* A broken companion is unavailable; it must never be treated as having placed a call. */ }
        if (!providerAvailable)
        {
            return "Nie wykonano połączenia. Ta wersja Sentinel X działa na Windows i nie ma aktywnego, " +
                   "zweryfikowanego mostu do telefonu/SIM ani dostępu do audio rozmowy. Nie wyszukiwałem numeru " +
                   "i nie rozpocząłem rozmowy. Potrzebny jest rzeczywisty companion Android z integracją " +
                   "telekomunikacyjną i obsługą audio; zwykła aplikacja Android nie może niezawodnie udostępnić " +
                   "Sentinelowi obu kanałów audio połączenia komórkowego. Nie będę symulować sukcesu.";
        }

        PhoneCallProviderResult result;
        try { result = await provider!.PlaceAndConductAsync(new PhoneCallRequest(raw), cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return "Nie mogę potwierdzić wykonania połączenia; zgłoszono błąd mostu telefonicznego: " + ex.Message;
        }

        // A request being accepted, a number being found, or a dial screen opening is not a completed call.
        if (!result.ActualCallCompleted)
            return "Połączenie nie zostało potwierdzone jako wykonane. Nie zgłaszam sukcesu." +
                   (string.IsNullOrWhiteSpace(result.Message) ? string.Empty : " Szczegóły: " + result.Message);

        if (string.IsNullOrWhiteSpace(result.Transcript))
            return "Most telefoniczny zgłosił zakończenie połączenia, ale nie dostarczył transkrypcji. " +
                   "Nie mogę zapisać wymaganej historii; wynik wymaga weryfikacji.";

        var record = new PhoneCallTranscript
        {
            Id = Guid.NewGuid().ToString("N"),
            CompletedAt = DateTimeOffset.Now,
            Request = raw,
            Summary = result.Summary?.Trim() ?? string.Empty,
            Transcript = result.Transcript.Trim()
        };
        try
        {
            // Once the call has ended, canceling the caller's request must not throw away its transcript.
            await history.AppendCompletedAsync(record, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return "Most telefoniczny potwierdził zakończone połączenie, ale nie udało się trwale zapisać " +
                   "transkrypcji. Nie podaję go jako w pełni zakończonej rezerwacji. Błąd zapisu: " + ex.Message;
        }

        return string.IsNullOrWhiteSpace(record.Summary)
            ? "Połączenie zakończone i transkrypcja zapisana."
            : record.Summary;
    }
}

/// <summary>Only a real handset/provider companion may implement this interface.</summary>
public interface IPhoneCallProvider
{
    bool IsAvailable { get; }
    Task<PhoneCallProviderResult> PlaceAndConductAsync(PhoneCallRequest request, CancellationToken cancellationToken);
}

public sealed record PhoneCallRequest(string UserRequest);

/// <param name="ActualCallCompleted">True only after the carrier call actually ended and the provider verified that fact.</param>
public sealed record PhoneCallProviderResult(
    bool ActualCallCompleted,
    string Message = "",
    string Summary = "",
    string Transcript = "");

public sealed class PhoneCallTranscript
{
    public string Id { get; init; } = string.Empty;
    public DateTimeOffset CompletedAt { get; init; }
    public string Request { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Transcript { get; init; } = string.Empty;
}

/// <summary>Append-only local JSONL archive. It accepts only completed calls with an actual transcript.</summary>
public sealed class PhoneCallTranscriptStore
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PhoneCallTranscriptStore(string? path = null) =>
        this.path = path ?? Path.Combine(AppPaths.HistoryDirectory, "phone-calls.jsonl");

    public async Task AppendCompletedAsync(PhoneCallTranscript record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.Transcript))
            throw new ArgumentException("Historia wymaga identyfikatora i transkrypcji rzeczywistej rozmowy.", nameof(record));

        await gate.WaitAsync(cancellationToken);
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string json = JsonSerializer.Serialize(record, JsonOptions);
            await File.AppendAllTextAsync(path, json + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<PhoneCallTranscript>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return [];
            var records = new List<PhoneCallTranscript>();
            foreach (string line in await File.ReadAllLinesAsync(path, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<PhoneCallTranscript>(line, JsonOptions);
                    if (item is not null && !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Transcript))
                        records.Add(item);
                }
                catch (JsonException) { /* One damaged row must not hide other saved transcripts. */ }
            }
            return records.OrderByDescending(x => x.CompletedAt).Take(limit).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<string> FormatRecentAsync(CancellationToken cancellationToken = default)
    {
        var records = await GetRecentAsync(cancellationToken: cancellationToken);
        if (records.Count == 0) return "Nie ma jeszcze zapisanych transkrypcji rzeczywiście wykonanych połączeń.";
        var output = new StringBuilder("HISTORIA POŁĄCZEŃ — ostatnie rozmowy\n");
        foreach (var item in records)
        {
            output.Append("\n").Append(item.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            if (!string.IsNullOrWhiteSpace(item.Summary)) output.Append(" · ").Append(item.Summary);
            output.Append("\nProśba: ").Append(item.Request).Append("\nTranskrypcja:\n").Append(item.Transcript).Append('\n');
        }
        return output.ToString().TrimEnd();
    }
}
