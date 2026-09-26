using System.Text.RegularExpressions;
using SentinelX.Core;

namespace SentinelX.Services.Intent;

/// <summary>Deterministic tools first. The language model cannot execute arbitrary commands.
/// Unknown input is repaired against the known-command catalogue before it reaches the model,
/// and the user is always told what was understood. Since 0.91, inputs that would otherwise fall
/// through to the AI produce a "did you mean…?" question instead of a silent guess, and every
/// successful repair is recorded in the local lessons journal („lekcje”).
/// Since 0.94 the router understands full sentences („sprawdź proszę ile mam ramu”), verb
/// synonyms („odpal discorda”) and embedded commands (CommandUnderstanding.Understand), keeps
/// recent tool readings in the session fact book for follow-up questions, and offers a read-only
/// dry-run („zrozum: …”) that shows every understanding step without executing anything.</summary>
public sealed class IntentRouter : IIntentRouter
{
    private readonly SentinelToolboxService toolbox;
    private readonly Services.Files.IFileService files;
    private readonly CommandRouter router;
    private readonly Services.Monitoring.ReadOnlyCommandService reads;
    private readonly UnderstandingJournal? journal;
    private readonly Services.Files.FileCleanupService? cleanup;
    private readonly SessionFactBook? facts;
    private readonly Core.LearnedPatterns? learned;
    private string? pendingSuggestion;
    private string? pendingSuggestionFrom;
    private string? currentInput;

    public IntentRouter(SentinelToolboxService toolbox, Services.Files.IFileService files,
        CommandRouter router, Services.Monitoring.ReadOnlyCommandService reads, UnderstandingJournal? journal = null,
        Services.Files.FileCleanupService? cleanup = null, SessionFactBook? facts = null, Core.LearnedPatterns? learned = null)
    {
        this.toolbox = toolbox; this.files = files; this.router = router; this.reads = reads; this.journal = journal;
        this.cleanup = cleanup; this.facts = facts; this.learned = learned;
        // The router raises this exactly when it would otherwise hand the input to the AI model.
        router.SuggestionPending += suggestion => { pendingSuggestion = suggestion; pendingSuggestionFrom = currentInput; };
    }

    public Task<string> ProcessAsync(string input, CancellationToken token) => ProcessAsync(input, token, null);

    public async Task<string> ProcessAsync(string input, CancellationToken token, Action<string>? onDelta)
    {
        token.ThrowIfCancellationRequested();
        input = CommandText.StripWakeWord(input);
        if (input.Length == 0) return "Słucham. Wpisz polecenie.";
        currentInput = input;

        // 0.94 · „zrozum: …” — dry-run rozumienia. Zawsze pierwsze, żeby polecenie w środku
        // (np. „zrozum: ile mam ramu”) nie wykonało się przez ekstrakcję.
        var dryRun = Regex.Match(input.Trim(), @"^zrozum\s*:?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(200));
        if (dryRun.Success) { pendingSuggestion = null; return CommandUnderstanding.Explain(dryRun.Groups[1].Value); }
        if (CommandText.Normalize(input) is "zrozum") return "Pokażę, jak rozumiem zdanie — bez wykonania.\nWpisz: zrozum: <Twoje zdanie>, np. „zrozum: sprawdź proszę ile mam ramu”.";
        if (CommandText.Normalize(input) is "fakty" or "ostatnie fakty")
        {
            pendingSuggestion = null;
            var snapshot = facts?.Snapshot() ?? [];
            return snapshot.Count == 0
                ? "Brak zapisanych odczytów z tej sesji. Fakty sesji powstają po odczytach typu „ile mam ramu” i żyją tylko do zamknięcia aplikacji."
                : "Fakty sesji (tylko w pamięci aplikacji, nie na dysku):\n" +
                  string.Join("\n", snapshot.Select(x => "· [" + x.Time + "] " + x.Label + ": " + x.Value));
        }

        // „//” palette resolution: a typed shortcut becomes the real command before anything else runs.
        string trimmed = input.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            var entry = SlashCatalog.TryResolve(trimmed[2..]);
            if (entry is { Kind: SlashKind.Command }) { pendingSuggestion = null; return await ProcessAsync(entry.Target, token, onDelta); }
            if (entry != null) return "„//" + entry.Trigger + "” to skrót interfejsu (" + entry.Label + ") — użyj palety // w polu wpisywania, aby go wybrać.";
            var closest = SlashCatalog.Filter(trimmed[2..]).Take(3).ToArray();
            return closest.Length == 0
                ? "Nie znam skrótu „" + trimmed + "”. Wpisz samo „//”, aby zobaczyć listę dostępnych poleceń."
                : "Nie znam skrótu „" + trimmed + "”. Najbliżej:\n" + string.Join("\n", closest.Select(x => "· //" + x.Trigger + " — " + x.Label)) + "\nWpisz samo „//”, aby zobaczyć całą listę.";
        }

        // A pending "did you mean…?" waits only for an explicit yes; anything else discards it.
        if (pendingSuggestion is { } suggested && CommandText.Normalize(input).Trim().TrimEnd('.', '!', '?', ',') is "tak" or "potwierdz" or "tak to")
        {
            pendingSuggestion = null;
            // 0.95: akceptacja propozycji to lekcja — para wejście → polecenie zostaje zapamiętana.
            if (pendingSuggestionFrom is { Length: > 0 } from) learned?.Record(from, suggested);
            pendingSuggestionFrom = null;
            return await ProcessAsync(suggested, token, onDelta);
        }
        pendingSuggestion = null;
        pendingSuggestionFrom = null;

        // 0.94: pełne rozumienie — skróty, literówki i polecenia ukryte w zdaniu.
        // 0.95: nauczone wzorce (z poprawek i akceptacji) rozpoznaję od razu, bez ponownego zgadywania.
        string effective = input;
        string note = "";
        if (learned != null && learned.TryGet(input, out string learnedTarget))
        {
            effective = learnedTarget;
            note = "Zrozumiałem jako: „" + learnedTarget + "” (nauczone z Twojej poprawki).\n\n";
        }
        else
        {
            var understanding = CommandUnderstanding.Understand(input);
            effective = understanding.Success ? understanding.Text : input;
            note = understanding.Success
                ? "Zrozumiałem jako: „" + understanding.Canonical + "” (" + understanding.Summary + ").\n\n"
                : "";
            if (understanding.Success)
            {
                journal?.Append(input.Trim(), understanding.Canonical, understanding.Summary);
                // Literówka/skrót z poprawką („→” w podsumowaniu) zostaje zapamiętana na przyszłość.
                if (understanding.Summary.Contains("→", StringComparison.Ordinal))
                    learned?.Record(input, understanding.Text);
            }
        }

        string? read = reads.Process(effective, token);
        if (read != null) { facts?.RecordResponse(effective, read); return note + read; }
        string? file = await files.ProcessAsync(effective, token);
        if (file != null) return note + file;
        string? cleanupResult = cleanup is null ? null : await cleanup.ProcessAsync(effective, token);
        if (cleanupResult != null) return note + cleanupResult;
        var result = await toolbox.ProcessAsync(effective, token);
        if (result.Handled)
        {
            facts?.RecordResponse(effective, result.Response);
            return note + result.Response;
        }
        return note + await router.ProcessAsync(effective, token, onDelta);
    }
}
