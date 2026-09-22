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
}
