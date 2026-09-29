using SentinelX.Core;
using SentinelX.Services.AI;
using System.Text.RegularExpressions;

namespace SentinelX.Services.Intent;

/// <summary>Deterministic tools first. The language model cannot execute arbitrary commands.
/// Unknown input is repaired against the known-command catalogue before it reaches the model,
/// and the user is always told what was understood. Since 0.91, inputs that would otherwise fall
/// through to the AI produce a "did you mean…?" question instead of a silent guess, and every
/// successful repair is recorded in the local lessons journal („lekcje”).</summary>
public sealed class IntentRouter : IIntentRouter
{
    private readonly SentinelToolboxService toolbox;
    private readonly Services.Files.IFileService files;
    private readonly CommandRouter router;
    private readonly Services.Monitoring.ReadOnlyCommandService reads;
    private readonly UnderstandingJournal? journal;
    private readonly Services.Files.FileCleanupService? cleanup;
    private readonly IAiService? ai;
    private string? pendingSuggestion;
    private DateTime lastDesktopContextAt;

    public IntentRouter(SentinelToolboxService toolbox, Services.Files.IFileService files,
        CommandRouter router, Services.Monitoring.ReadOnlyCommandService reads, UnderstandingJournal? journal = null,
        Services.Files.FileCleanupService? cleanup = null, IAiService? ai = null)
    {
        this.toolbox = toolbox; this.files = files; this.router = router; this.reads = reads; this.journal = journal; this.cleanup = cleanup; this.ai = ai;
        // The router raises this exactly when it would otherwise hand the input to the AI model.
        router.SuggestionPending += suggestion => pendingSuggestion = suggestion;
    }

    public Task<string> ProcessAsync(string input, CancellationToken token) => ProcessAsync(input, token, null);

    public async Task<string> ProcessAsync(string input, CancellationToken token, Action<string>? onDelta)
    {
        token.ThrowIfCancellationRequested();
        input = CommandText.StripWakeWord(input);
        if (input.Length == 0) return "Słucham. Wpisz polecenie.";

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
            return await ProcessAsync(suggested, token, onDelta);
        }
        pendingSuggestion = null;

        // Typos and short forms: repair only rewrites the text, then the normal pipeline decides.
        var repair = CommandUnderstanding.Repair(input);
        string effective = repair.Success ? repair.Text : input;
        string note = repair.Success
            ? "Zrozumiałem jako: „" + repair.Canonical + "” (" + repair.Summary + ").\n\n"
            : "";
        if (repair.Success) journal?.Append(input.Trim(), repair.Canonical, repair.Summary);

        bool desktopQuestion = IsDesktopContextQuestion(effective);
        bool desktopFollowUp = !desktopQuestion && DateTime.Now - lastDesktopContextAt <= TimeSpan.FromMinutes(3) && IsDesktopContextFollowUp(effective);
        if (desktopQuestion || desktopFollowUp)
        {
            string visibleControls = toolbox.ReadDesktopContext(effective);
            if (visibleControls.StartsWith("Nie mam świeżego", StringComparison.OrdinalIgnoreCase) ||
                visibleControls.StartsWith("Nie udało", StringComparison.OrdinalIgnoreCase) ||
                visibleControls.StartsWith("Nie można", StringComparison.OrdinalIgnoreCase) ||
                visibleControls.StartsWith("UI Automation", StringComparison.OrdinalIgnoreCase) ||
                visibleControls.StartsWith("Okno ", StringComparison.OrdinalIgnoreCase) && visibleControls.Contains("nie udostępnia tekstowych kontrolek", StringComparison.OrdinalIgnoreCase))
            { lastDesktopContextAt = default; return note + visibleControls; }
            if (desktopQuestion) lastDesktopContextAt = DateTime.Now;
            if (IsDirectScreenRead(effective) || ai == null) return note + visibleControls;
            string guidance = "Odpowiadaj po polsku wyłącznie na podstawie ograniczonego tekstowego kontekstu UI Automation poniżej. To NIE jest zrzut ekranu: nie twierdź, że widzisz piksele, obrazy, układ ani treść niewymienioną w kontekście. Jeśli etykiety nie wystarczają, powiedz to wprost. Nie wykonuj żadnych działań; możesz tylko wyjaśnić widoczny komunikat i zaproponować bezpieczny następny krok.\n\nKontekst UI Automation:\n" + visibleControls;
            return note + await ai.AskAsync(effective, guidance, token);
        }
        lastDesktopContextAt = default;

        string? read = reads.Process(effective, token);
        if (read != null) return note + read;
        string? file = await files.ProcessAsync(effective, token);
        if (file != null) return note + file;
        string? cleanupResult = cleanup is null ? null : await cleanup.ProcessAsync(effective, token);
        if (cleanupResult != null) return note + cleanupResult;
        var result = await toolbox.ProcessAsync(effective, token);
        return result.Handled ? note + result.Response : note + await router.ProcessAsync(effective, token, onDelta);
    }

    private static bool IsDesktopContextQuestion(string input)
    {
        string query = CommandText.Normalize(input).Trim().TrimEnd('.', '!', '?', ',');
        return query is "co jest na ekranie" or "co widzisz na ekranie" or "odczytaj ekran" or "przeczytaj ekran" or
            "pokaz tekst z ekranu" or "co jest w tym oknie" or "opisz to okno" or "pomoz z tym oknem" or
            "co to za blad" or "wyjasnij ten blad" or "co oznacza ten komunikat" or "wyjasnij ten komunikat" or
            "jaki komunikat widzisz" or "co jest napisane na ekranie" or "przeczytaj komunikat na ekranie" or
            "what is on screen" or "what do you see on screen" or "read the screen" or "what is in this window" or
            "what is this error" or "explain this error" or "help with this window" ||
            Regex.IsMatch(query, @"^(?:co to za|wyjasnij|co oznacza) (?:blad|komunikat)(?: na ekranie| w oknie)?$");
    }

    private static bool IsDesktopContextFollowUp(string input)
    {
        string query = CommandText.Normalize(input).Trim().TrimEnd('.', '!', '?', ',');
        return query is "jak to naprawic" or "co mam kliknac" or "co kliknac" or "co dalej" or "co teraz" or
            "jaki nastepny krok" or "co powinienem zrobic" or "jak przejsc dalej" or "wyjasnij to dokladniej" or
            "how do i fix this" or "what should i click" or "what next" or "what should i do now" or "explain that further";
    }

    private static bool IsDirectScreenRead(string input)
    {
        string query = CommandText.Normalize(input).Trim().TrimEnd('.', '!', '?', ',');
        return query is "odczytaj ekran" or "przeczytaj ekran" or "pokaz tekst z ekranu" or "co jest na ekranie" or "co widzisz na ekranie" or
            "read the screen" or "what is on screen" or "what do you see on screen";
    }
}
