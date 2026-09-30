using SentinelX.Services.Permissions;
namespace SentinelX.Services.Memory;

/// <summary>Destructive memory operations share the same pending slot, cancellation and audit as other tools.</summary>
public sealed class MemoryActionService(ConversationMemoryService memory, IPermissionService permissions, ActionHistoryService history)
{
    public static bool IsMutation(string input) => Parse(input).Type != null;
    private static (string? Type, string Argument, string Description) Parse(string input)
    {
        string text = Core.CommandText.Normalize(input);
        if (text is "usun wszystkie wspomnienia" or "wyczysc cala pamiec")
            return ("MEMORY_CLEAR_ALL", "", "Usunąć historię rozmowy, profil i wszystkie wspomnienia? Eksporty i audyt akcji pozostaną.");
        if (text == "wyczysc pamiec rozmowy")
            return ("MEMORY_CLEAR_CHAT", "", "Usunąć historię rozmowy? Trwałe wspomnienia i profil pozostaną.");
        if (text.StartsWith("zapomnij ", StringComparison.Ordinal))
            return ("MEMORY_FORGET", text[9..].Trim(), $"Usunąć z pamięci i rozmowy wpisy pasujące do: {text[9..].Trim()}? Eksporty pozostaną.");
        return (null, "", "");
    }
    public string? TryRequest(string input)
    {
        var mutation = Parse(input);
        if (mutation.Type == null) return null;
        if (mutation.Type == "MEMORY_FORGET" && IsContextualReference(mutation.Argument))
        {
            if (!memory.TryGetPreviousUserMessage(input, out string previous))
                return "Nie umiem wskazać, którą informację zapomnieć. Podaj jej temat albo dokładny fragment.";
            var candidates = memory.FindRelevantNotes(previous, 3).ToArray();
            if (candidates.Length != 1)
                return candidates.Length == 0 ? "Nie znalazłem trwałego wspomnienia z poprzedniej wypowiedzi. Podaj temat lub dokładny fragment; niczego nie usunąłem."
                    : "Poprzednia wypowiedź pasuje do kilku trwałych wspomnień. Podaj dokładniejszy temat, żebym nie wskazał niewłaściwego wpisu.";
            string preview = candidates[0].Text.Length <= 100 ? candidates[0].Text : candidates[0].Text[..97] + "…";
            mutation = (mutation.Type, candidates[0].Text,
                $"Usunąć trwałe wspomnienie wskazane w poprzedniej wypowiedzi: „{preview}”? Usunięcie wymaga potwierdzenia.");
        }
        if (mutation.Type == "MEMORY_FORGET" && (mutation.Argument.Length == 0 || mutation.Argument.Length > 500))
            return "Podaj fragment wspomnienia (1–500 znaków).";
        string id = history.CreateActionId();
        var action = new PendingPermissionAction
        {
            ActionId = id, ActionType = mutation.Type, OriginalCommand = input,
            Description = mutation.Description, RiskLevel = "HIGH",
            CancellableExecutor = token =>
            {
                token.ThrowIfCancellationRequested();
                if (mutation.Type == "MEMORY_CLEAR_ALL") memory.ClearAll();
                else if (mutation.Type == "MEMORY_CLEAR_CHAT") memory.Clear();
                else memory.Forget(mutation.Argument);
                // Verify persisted state immediately, before the assistant adds the new acknowledgement.
                bool verified = memory.VerifyPersistedState(out string evidence);
                return Task.FromResult(verified
                    ? ActionExecutionResult.VerifiedSuccess("Usunięcie wykonane i zapis pamięci sprawdzony. Eksporty i audyt nie zostały usunięte.", evidence)
                    : ActionExecutionResult.Failure("Zmieniono pamięć procesu, ale nie potwierdzono zapisu. Stare dane mogą powrócić po restarcie.", evidence));
            }
        };
        if (permissions.TryRequest(action, out string response)) history.AddPending(id, action.ActionType, input, action.Description);
        return response;
    }

    private static bool IsContextualReference(string argument)
    {
        string normalized = Core.CommandText.Normalize(argument).Trim().Trim('?', '.', '!', '"', '„', '”');
        return normalized is "to" or "to wspomnienie" or "te informacje" or "ta informacja" or
            "te wspomnienie" or "tamto" or "tamta informacja" or "this" or "that" or "this information" or "that information" or
            "this info" or "that info" or "this memory";
    }

    /// <summary>Deleting one explicit memory by stable ID is still HIGH risk and goes through the same approval flow.</summary>
    public string RequestDeleteNote(string noteId, string preview)
    {
        var note = memory.FindNote(noteId);
        if (note == null) return "Wspomnienie już nie istnieje. Odśwież listę.";
        string id = history.CreateActionId();
        string shortText = preview.Length <= 80 ? preview : preview[..77] + "…";
        var action = new PendingPermissionAction
        {
            ActionId = id, ActionType = "MEMORY_DELETE_NOTE", OriginalCommand = "usuń wspomnienie",
            Description = $"Usunąć jedno wspomnienie: „{shortText}”? Dotyczy wyłącznie tego wpisu — podobne teksty pozostaną.",
            RiskLevel = "HIGH",
            CancellableExecutor = token =>
            {
                token.ThrowIfCancellationRequested();
                if (!memory.DeleteNote(noteId))
                    return Task.FromResult(ActionExecutionResult.Failure("Wspomnienie nie istniało w chwili zatwierdzenia. Nic nie usunięto.", "Odczyt przed wykonaniem nie znalazł wpisu o tym identyfikatorze."));
                bool verified = memory.VerifyPersistedState(out string evidence);
                return Task.FromResult(verified
                    ? ActionExecutionResult.VerifiedSuccess("Usunięto jedno wspomnienie i sprawdzono zapis. Pozostałe wpisy i audyt zachowane.", evidence)
                    : ActionExecutionResult.Failure("Zmieniono pamięć procesu, ale nie potwierdzono zapisu. Stare dane mogą powrócić po restarcie.", evidence));
            }
        };
        if (permissions.TryRequest(action, out string response)) history.AddPending(id, action.ActionType, "usuń wybrane wspomnienie", action.Description);
        return response;
    }

    // -------- verified non-destructive mutations for the Memory page (audited, read-back checked) --------
    public string AddNoteVerified(string text, string category, string source)
    {
        var result = memory.AddNote(text, category, source);
        string id = history.CreateActionId();
        return result switch
        {
            NoteAddResult.Added => Audited(id, "MEMORY_ADD", "dodaj wspomnienie", true, "Zapisano wspomnienie.", VerifiedEvidence()),
            NoteAddResult.StaleDuplicate => Audited(id, "MEMORY_ADD", "dodaj wspomnienie", true, "Takie wspomnienie istniało jako nieaktualne — przywrócono je zamiast tworzyć duplikat.", VerifiedEvidence()),
            NoteAddResult.Updated => Audited(id, "MEMORY_EDIT", "zaktualizuj wspomnienie", true, "Zaktualizowano bieżącą wartość wspomnienia; stara wartość pozostała w dzienniku zmian.", VerifiedEvidence()),
            NoteAddResult.Duplicate => "Takie wspomnienie już istnieje. Nic nie dodano (ochrona przed duplikatami).",
            NoteAddResult.Disabled => memory.LastStorageError ?? "Zapisywanie wspomnień jest wyłączone w ustawieniach prywatności.",
            _ => memory.LastStorageError ?? "Nie zapisano wspomnienia."
        };
    }

    public string UpdateNoteVerified(string noteId, string newText)
        => Audited(history.CreateActionId(), "MEMORY_EDIT", "edytuj wspomnienie", memory.UpdateNote(noteId, newText),
            "Zaktualizowano wspomnienie.", memory.LastStorageError ?? "Nie znaleziono wspomnienia lub treść jest niepoprawna.");

    public string SetPinnedVerified(string noteId, bool pinned)
        => Audited(history.CreateActionId(), "MEMORY_PIN", "przypnij wspomnienie", memory.SetPinned(noteId, pinned),
            pinned ? "Przypięto wspomnienie — trafia do kontekstu w pierwszej kolejności." : "Odpięto wspomnienie.",
            "Nie znaleziono wspomnienia.");

    public string SetStaleVerified(string noteId, bool stale)
        => Audited(history.CreateActionId(), "MEMORY_STALE", "oznacz aktualność", memory.SetStale(noteId, stale),
            stale ? "Oznaczono jako nieaktualne — wspomnienie nie trafia już do kontekstu AI, ale nie jest skasowane." : "Przywrócono wspomnienie jako aktualne.",
            "Nie znaleziono wspomnienia.");

    public string ImportVerified(string path)
    {
        string id = history.CreateActionId();
        string summary = memory.ImportMemories(path);
        bool ok = summary.StartsWith("Zaimportowano", StringComparison.Ordinal);
        history.AddResult(id, "MEMORY_IMPORT", "importuj wspomnienia", ok
            ? ActionExecutionResult.VerifiedSuccess(summary, VerifiedEvidence())
            : ActionExecutionResult.Failure(summary, "Import przerwany przed zapisem — plik pamięci nie został zmieniony."));
        return summary;
    }

    private string Audited(string id, string type, string command, bool ok, string success, string failure)
    {
        if (!ok) return failure;
        bool verified = memory.VerifyPersistedState(out string evidence);
        history.AddResult(id, type, command, verified
            ? ActionExecutionResult.VerifiedSuccess(success, evidence)
            : ActionExecutionResult.Failure("Zmiana w pamięci procesu, ale zapis na dysk nie został potwierdzony. Sprawdź po restarcie.", evidence));
        return verified ? success : "Zmiana wykonana, ale zapisu nie potwierdzono: " + evidence;
    }

    private string VerifiedEvidence() => memory.VerifyPersistedState(out string evidence) ? evidence : "Brak odczytu zwrotnego: " + evidence;
}
