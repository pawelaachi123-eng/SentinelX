using System.IO;
using System.Text.Json;

namespace SentinelX.Tests;

/// <summary>Tasks and reminders: parser truth table, accept-only flow, persistence, due/missed semantics.</summary>
internal static class TaskRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var now = new DateTime(2026, 9, 23, 16, 0, 0); // Wednesday 16:00

        // --- parser truth table (absolute instants, local time) ---
        Check(PolishTimeParser.TryParse("za 30 minut", now, out var w1, out _) && w1 == now.AddMinutes(30), "za 30 minut");
        Check(PolishTimeParser.TryParse("za 2 godziny", now, out var w2, out _) && w2 == now.AddHours(2), "za 2 godziny");
        Check(PolishTimeParser.TryParse("za pół godziny", now, out var w3, out _) && w3 == now.AddMinutes(30), "za pół godziny");
        Check(PolishTimeParser.TryParse("jutro o 18", now, out var w4, out _) && w4 == now.Date.AddDays(1).AddHours(18), "jutro o 18");
        Check(PolishTimeParser.TryParse("jutro", now, out var w5, out _) && w5 == now.Date.AddDays(1).AddHours(9), "jutro without a time defaults to 09:00");
        Check(PolishTimeParser.TryParse("pojutrze o 8:30", now, out var w6, out _) && w6 == now.Date.AddDays(2).AddHours(8).AddMinutes(30), "pojutrze o 8:30");
        Check(PolishTimeParser.TryParse("dziś o 19", now, out var w7, out _) && w7 == now.Date.AddHours(19), "dziś o 19");
        Check(!PolishTimeParser.TryParse("dziś o 15", now, out _, out string pastError) && pastError.Contains("minęła"), "past hour today is refused with an explanation");
        Check(!PolishTimeParser.TryParse("dziś", now, out _, out string noHour) && noHour.Contains("godzinę"), "dziś without a time asks for an hour");
        // Wednesday 16:00 → „w piątek” = this Friday; „w środę” alone = next week (7 days).
        Check(PolishTimeParser.TryParse("w piątek o 15", now, out var w8, out _) && w8 == new DateTime(2026, 9, 25, 15, 0, 0), "w piątek o 15");
        Check(PolishTimeParser.TryParse("w środę", now, out var w9, out _) && w9 == new DateTime(2026, 9, 30, 9, 0, 0), "w środę alone means next week");
        Check(PolishTimeParser.TryParse("w środę o 18", now, out var w10, out _) && w10 == new DateTime(2026, 9, 23, 18, 0, 0), "same-day weekday with a future hour stays today");
        Check(PolishTimeParser.TryParse("w środę o 15", now, out var w10b, out _) && w10b == new DateTime(2026, 9, 30, 15, 0, 0), "same-day weekday with a past hour rolls to next week");
        Check(PolishTimeParser.TryParse("w piątek 15:00", now, out var w11, out _) && w11 == new DateTime(2026, 9, 25, 15, 0, 0), "weekday with HH:MM without o");
        Check(PolishTimeParser.TryParse("24.12 o 12", now, out var w12, out _) && w12 == new DateTime(2026, 12, 24, 12, 0, 0), "explicit date with time");
        Check(PolishTimeParser.TryParse("12.06 o 18", now, out var w13, out _) && w13.Month == 6 && w13.Hour == 18, "12.06 o 18 means June 12 at 18:00, never 12:06");
        Check(PolishTimeParser.TryParse("10.03", now, out var w14, out _) && w14 == new DateTime(2027, 3, 10, 9, 0, 0), "past date without a year rolls to next year");
        Check(!PolishTimeParser.TryParse("blabla", now, out _, out string help) && help.Contains("Nie rozpoznano"), "gibberish is refused with examples");

        // --- task CRUD + persistence across a reload ---
        string dir = Path.Combine(directory, "tasks");
        var tasks = new TaskService(dir) { NowProvider = () => now };
        var task = tasks.AddTask("Wysłać raport ZXCVBNM", TaskRecord.PriorityHigh, now.Date.AddDays(1).AddHours(12), "");
        Check(task != null && tasks.GetTasks().Single().Title.StartsWith("Wysłać"), "task is stored");
        Check(tasks.AddTask("", TaskRecord.PriorityNormal, null, "") == null, "empty title refused");
        var reminder = tasks.AddReminder("Zadzwoń do mamy", now.AddHours(2), "");
        Check(reminder != null, "future reminder is stored");
        Check(tasks.AddReminder("minęło", now.AddMinutes(-1), "") == null, "past reminder refused");
        Check(!tasks.CheckDue().Any(), "nothing due yet");

        var reloaded = new TaskService(dir) { NowProvider = () => now };
        Check(reloaded.GetTasks().Single().Title.Contains("ZXCVBNM"), "tasks survive a reload");
        Check(reloaded.GetReminders().Single().Text == "Zadzwoń do mamy", "reminders survive a reload");
        Check(reloaded.VerifyPersistedState(out string evidence), "store verifies with read-back evidence: " + evidence);

        // --- status cycle ---
        Check(reloaded.SetTaskStatus(task!.Id, TaskRecord.StatusDoing) && reloaded.GetTasks().Single().Status == TaskRecord.StatusDoing, "open → w toku");
        Check(reloaded.SetTaskStatus(task.Id, TaskRecord.StatusDone) && reloaded.GetTasks(includeDone: true).Single().Status == TaskRecord.StatusDone, "w toku → zrobione");
        Check(reloaded.GetTasks().Count == 0 && reloaded.GetTasks(includeDone: true).Count == 1, "done tasks leave the open list");
        Check(reloaded.SetTaskStatus(task.Id, TaskRecord.StatusOpen), "done can be restored");

        // --- due reminders fire once; at startup overdue ones surface as missed ---
        var firedLog = new List<string>();
        reloaded.ReminderFired += line => firedLog.Add(line);
        reloaded.NowProvider = () => now.AddHours(3); // past the 18:00 reminder
        var fired = reloaded.CheckDue();
        Check(fired.Count == 1 && fired[0].Text == "Zadzwoń do mamy" && !fired[0].Missed, "due reminder fires, not marked missed while app was running");
        Check(firedLog.Count == 1 && firedLog[0].Contains("Zadzwoń do mamy"), "reminder event reaches subscribers");
        Check(!reloaded.CheckDue().Any(), "a delivered reminder never fires twice");

        // app "closed" before the reminder: load fresh state where the clock jumped past RemindAt
        var lateService = new TaskService(Path.Combine(directory, "tasks-late")) { NowProvider = () => now };
        lateService.AddReminder("Włącz piekarnik", now.AddMinutes(30), "");
        lateService.NowProvider = () => now.AddHours(2); // simulate reopening later
        var missed = lateService.CheckDue(atStartup: true);
        Check(missed.Count == 1 && missed[0].Missed && missed[0].NotifiedAt != null, "startup marks overdue reminders as missed, never silently skipped");

        // --- deletion is single-record ---
        Check(reloaded.DeleteTask("nieznany-id") == false, "unknown id deletes nothing");
        Check(reloaded.DeleteTask(task.Id) && reloaded.GetTasks(includeDone: true).Count == 0, "single delete works");
        Check(new TaskService(dir).GetTasks(includeDone: true).Count == 0, "delete persists after reload");
        await Task.CompletedTask;
    }
}
