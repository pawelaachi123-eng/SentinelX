using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SentinelX
{
    public sealed class ActionHistoryEntry
    {
        public string ActionId { get; set; } =
            string.Empty;

        public DateTime Timestamp { get; set; }

        public string ActionType { get; set; } =
            string.Empty;

        public string Command { get; set; } =
            string.Empty;

        public string Status { get; set; } =
            string.Empty;

        public string Message { get; set; } =
            string.Empty;

        public string Evidence { get; set; } =
            string.Empty;

        public string RequestId { get; set; } = string.Empty;
        public string ParentActionId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public long DurationMilliseconds { get; set; }
        public string RecoveryAdvice { get; set; } = string.Empty;
    }


    public sealed class ActionHistoryService
    {
        private static readonly string sessionId = Guid.NewGuid().ToString("N");
        public string? LastStorageError { get; private set; }
        public string? LastReadError { get; private set; }
        public string HistoryPath => historyPath;
        private readonly object syncRoot =
            new object();

        private readonly string historyDirectory;

        private readonly string historyPath;

        private readonly JsonSerializerOptions jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase
            };


        public ActionHistoryService(string? dataDirectory = null)
        {
            historyDirectory =
                Path.Combine(dataDirectory ?? AppPaths.Root,
                    "History");


            historyPath =
                Path.Combine(
                    historyDirectory,
                    "actions.jsonl");


            Directory.CreateDirectory(
                historyDirectory);
            RecoverInterruptedActions();
        }

        public void AddRunning(string actionId, string actionType, string command, string parentActionId = "") => AddEntry(new()
        {
            ActionId = actionId, Timestamp = DateTime.Now, ActionType = actionType, Command = command,
            ParentActionId = parentActionId, Status = "RUNNING", Message = "Wykonywanie akcji.", Evidence = "Wynik nie został jeszcze potwierdzony."
        });

        public IReadOnlyList<ActionHistoryEntry> GetRecentEntries(int count = 50) => LoadLatestStates()
            .OrderByDescending(x => x.Timestamp).Take(Math.Clamp(count, 1, 200)).ToArray();

        public string GetActionDetails(string actionId)
        {
            var entry = LoadLatestStates().FirstOrDefault(x => x.ActionId.Equals(actionId.Trim(), StringComparison.OrdinalIgnoreCase));
            return entry == null ? "Nie znaleziono akcji o tym identyfikatorze." :
                $"{entry.ActionId} [{entry.Status}]\n{entry.ActionType} · {entry.Timestamp:yyyy-MM-dd HH:mm:ss}\n{entry.Message}\nDowód: {entry.Evidence}\nCzas: {entry.DurationMilliseconds} ms\nDalszy krok: {(entry.RecoveryAdvice.Length == 0 ? "brak" : entry.RecoveryAdvice)}";
        }

        private void RecoverInterruptedActions()
        {
            foreach (var entry in LoadLatestStates().Where(x => x.SessionId != sessionId && x.Status is "RUNNING" or "PENDING"))
            {
                bool running = entry.Status == "RUNNING";
                entry.Status = running ? "INTERRUPTED" : "EXPIRED";
                entry.Timestamp = DateTime.Now;
                entry.Message = running ? "Poprzednia sesja zakończyła się bez potwierdzonego wyniku." : "Zgoda z poprzedniej sesji wygasła.";
                entry.Evidence = running ? "Brak końcowego zapisu. Stan aplikacji wymaga sprawdzenia; akcja nie zostanie automatycznie powtórzona." : "Po restarcie wymagane jest nowe polecenie i potwierdzenie.";
                entry.RecoveryAdvice = "Sprawdź stan docelowej aplikacji przed ponowieniem.";
                AddEntry(entry);
            }
        }


        public string CreateActionId()
        {
            string random =
                Guid.NewGuid()
                    .ToString("N")
                    .Substring(0, 6)
                    .ToUpperInvariant();


            return
                $"SX-{DateTime.Now:yyyyMMdd-HHmmss}-{random}";
        }


        public void AddPending(
            string actionId,
            string actionType,
            string command,
            string message)
        {
            AddEntry(
                new ActionHistoryEntry
                {
                    ActionId =
                        actionId,

                    Timestamp =
                        DateTime.Now,

                    ActionType =
                        actionType,

                    Command =
                        command,

                    Status =
                        "PENDING",

                    Message =
                        message,

                    Evidence =
                        "Oczekuje na zgodę użytkownika."
                });
        }


        public void AddCancelled(
            string actionId,
            string actionType,
            string command,
            string evidence = "Nie wykonano zmian.")
        {
            AddEntry(
                new ActionHistoryEntry
                {
                    ActionId =
                        actionId,

                    Timestamp =
                        DateTime.Now,

                    ActionType =
                        actionType,

                    Command =
                        command,

                    Status =
                        "CANCELLED",

                    Message =
                        "Akcja anulowana przez użytkownika.",

                    Evidence =
                        evidence
                });
        }


        public void AddResult(
            string actionId,
            string actionType,
            string command,
            ActionExecutionResult result,
            long durationMilliseconds = 0,
            string parentActionId = "")
        {
            string status = result.Status;


            AddEntry(
                new ActionHistoryEntry
                {
                    ActionId =
                        actionId,

                    Timestamp =
                        DateTime.Now,

                    ActionType =
                        actionType,

                    Command =
                        command,

                    Status =
                        status,

                    Message =
                        result.Message,

                    Evidence =
                        result.Evidence,
                    ParentActionId = parentActionId,
                    DurationMilliseconds = durationMilliseconds,
                    RecoveryAdvice = result.RecoveryAdvice
                });
        }


        private void AddEntry(
            ActionHistoryEntry entry)
        {
            lock (syncRoot)
            {
                try
                {
                    entry.SessionId = sessionId;
                    if (entry.ActionType == "REQUEST") entry.RequestId = entry.ActionId;
                    else Services.History.ActionEvidenceCapture.Record(entry);
                    string json =
                        JsonSerializer.Serialize(
                            entry,
                            jsonOptions);


                    File.AppendAllText(
                        historyPath,
                        json +
                        Environment.NewLine);
                    LastStorageError = null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LastStorageError = "Nie udało się zapisać historii: " + ex.Message;
                }
            }
        }


        public string GetRecentSummary(
            int count = 10)
        {
            List<ActionHistoryEntry> entries =
                LoadLatestStates();


            if (entries.Count == 0)
            {
                return
                    "Brak zapisanych akcji.";
            }


            StringBuilder result =
                new StringBuilder();


            result.AppendLine(
                "OSTATNIE AKCJE");


            result.AppendLine();


            foreach (
                ActionHistoryEntry entry
                in entries
                    .OrderByDescending(
                        x => x.Timestamp)
                    .Take(count))
            {
                result.AppendLine(
                    $"{entry.Timestamp:HH:mm:ss}  [{entry.Status}]");


                result.AppendLine(
                    entry.ActionId);


                result.AppendLine(
                    entry.Message);


                if (!string.IsNullOrWhiteSpace(
                        entry.Evidence))
                {
                    result.AppendLine(
                        $"Dowód: {entry.Evidence}");
                }


                result.AppendLine();
            }


            if (LastStorageError != null) result.AppendLine(LastStorageError);
            return result
                .ToString()
                .Trim();
        }


        private List<ActionHistoryEntry>
            LoadLatestStates()
        {
            lock (syncRoot)
            {
                LastReadError = null;
                Dictionary<string, ActionHistoryEntry> latest =
                    new Dictionary<string, ActionHistoryEntry>(
                        StringComparer.OrdinalIgnoreCase);


                try
                {
                    if (!File.Exists(
                            historyPath))
                    {
                        return new List<ActionHistoryEntry>();
                    }


                    foreach (
                        string line
                        in File.ReadLines(
                            historyPath))
                    {
                        if (string.IsNullOrWhiteSpace(
                                line))
                        {
                            continue;
                        }


                        try
                        {
                            ActionHistoryEntry? entry =
                                JsonSerializer.Deserialize<ActionHistoryEntry>(
                                    line,
                                    jsonOptions);


                            if (entry == null ||
                                string.IsNullOrWhiteSpace(
                                    entry.ActionId))
                            {
                                LastReadError = "Wpis historii bez identyfikatora akcji.";
                                continue;
                            }


                            latest[entry.ActionId] =
                                entry;
                        }
                        catch (JsonException ex)
                        {
                            LastReadError = "Uszkodzony wpis historii: " + ex.Message;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LastReadError = "Nie odczytano historii: " + ex.Message;
                }


                return latest
                    .Values
                    .ToList();
            }
        }
    }
}
