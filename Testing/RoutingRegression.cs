using System.IO;
using SentinelX.Core;
using SentinelX.Services.Intent;
using SentinelX.Services.Monitoring;
namespace SentinelX.Tests;

/// <summary>0.96 · KUŹNIA: the decision preview („jak to rozumiem: …”) and the task search, both through the real
/// IntentRouter → CommandRouter pipeline. The preview must never execute, write or reach the model.</summary>
internal static class RoutingRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static int QrFiles() => Directory.Exists(Path.Combine(AppPaths.Root, "Qr")) ? Directory.GetFiles(Path.Combine(AppPaths.Root, "Qr")).Length : 0;

    public static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var history = new ActionHistoryService(root);
        var memory = new ConversationMemoryService(Path.Combine(root, "Memory"));
        var toolbox = new SentinelToolboxService(history: history, permissions: new PermissionCenterService(), memory: memory, historyExport: new Services.History.HistoryExportService(history, root));
        using var monitor = new SystemMonitor();
        using var localAi = new LocalAiService(new GamingModeService());
        var tasks = new TaskService(Path.Combine(root, "tasks"));
        var commandRouter = new CommandRouter(monitor, new SystemInfoService(), localAi, memory, tasks: tasks);
        var router = new IntentRouter(toolbox, new FileWorkspaceService(root, root, history), commandRouter, new ReadOnlyCommandService(monitor, history));

        // --- decision preview: what Sentinel would do, without doing it ---
        string tool = DecisionPreview.TryExplain("jak to rozumiem: kwota slownie: 1234,56") ?? throw new InvalidOperationException("The preview trigger was not recognised.");
        Check(tool.Contains("Narzędzie „Kwota słownie”") && tool.Contains("To tylko podgląd"), "A tool command must be described as that tool: " + tool);
        Check(!tool.Contains("złote 56 groszy"), "The preview must not run the tool (no result in the explanation): " + tool);
        string abbreviation = DecisionPreview.TryExplain("Jak to rozumiem: dk")!;
        Check(abbreviation.Contains("diagnostyka komputera") && abbreviation.Contains("Literówka lub skrót"), "An abbreviation must be shown as its expansion: " + abbreviation);
        string missing = DecisionPreview.TryExplain("jak to rozumiem: unix:")!;
        Check(missing.Contains("Brakuje argumentu") && missing.Contains("unix: 1700000000"), "A tool without its argument must show an example: " + missing);
        string risky = DecisionPreview.TryExplain("jak to rozumiem: usun duplikaty c:\\dane")!;
        Check(risky.Contains("zgody") && risky.Contains("niczego nie wykonałem"), "A destructive command must be described as needing consent: " + risky);
        string question = DecisionPreview.TryExplain("jak to rozumiem: opowiedz mi proszę coś ciekawego o historii Krakowa")!;
        Check(question.Contains("zwykłe pytanie") && question.Contains("modelu AI"), "Free text must be described as a model question: " + question);
        string nested = DecisionPreview.TryExplain("jak to rozumiem: jak to rozumiem: lotto")!;
        Check(nested.Contains("nie zagnieżdżam"), "Previews must not nest: " + nested);
        Check(DecisionPreview.TryExplain("jak to rozumiem")!.Contains("Napisz"), "A bare trigger must show how to use it.");
        Check(DecisionPreview.TryExplain("Jak rozumiesz słowo ambicja?") == null, "An ordinary question must not be hijacked by the preview.");
        Check(DecisionPreview.TryExplain("ile mam ramu") == null, "A normal command must not be taken for a preview request.");

        // The catalogue and the preview agree: every entry resolves to itself (no tool shadows another one).
        foreach (var entry in ToolCatalog.Entries)
        {
            string command = ConversationMemoryService.Normalize(ToolCatalog.BuildCommand(entry, entry.Example)).TrimEnd('?', '!', '.', ' ');
            string? resolved = DecisionPreview.FindTool(command)?.Id;
            Check(resolved == entry.Id, "Catalogue entry „" + entry.Id + "” resolved to „" + resolved + "” for: " + command);
        }
        // Every new Forge tool is really handled by the toolbox (the page may never offer what the router cannot do).
        foreach (var entry in ToolCatalog.Entries.Where(x => x.Category == "Kuźnia 0.96"))
        {
            string command = ToolCatalog.BuildCommand(entry, entry.Example);
            string? answer = UtilityToolbox.Process(command, ConversationMemoryService.Normalize(command).TrimEnd('?', '!', '.', ' '));
            Check(answer != null && answer.Length > 0, "The Forge tool „" + entry.Id + "” is in the catalogue but the router does not handle: " + command);
        }

        // --- the preview through the real pipeline: no side effects, a QR preview writes no PNG ---
        int pngBefore = QrFiles();
        string qrPreview = await router.ProcessAsync("jak to rozumiem: qr: https://example.com", default);
        Check(qrPreview.Contains("podgląd niczego nie zapisuje") && !qrPreview.Contains("Kod QR zapisany"), "The QR preview must only describe: " + qrPreview);
        Check(QrFiles() == pngBefore, "The decision preview must not create a PNG file.");
        string wifiPreview = await router.ProcessAsync("jak to rozumiem: qr wifi: Siec|haslo", default);
        Check(wifiPreview.Contains("Kod QR do sieci Wi-Fi") && QrFiles() == pngBefore, "The Wi-Fi QR preview must be a description too: " + wifiPreview);
        string viaRouter = await commandRouter.ProcessAsync("jak to rozumiem: lotto", default);
        Check(viaRouter.Contains("Narzędzie „Lotto”") && !viaRouter.Contains("Lotto (6 z 49)"), "The command router must preview without running: " + viaRouter);

        // --- search in tasks: local, read-only, diacritics-insensitive, includes done tasks and reminders ---
        var open = tasks.AddTask("Wysłać raport kwartalny", TaskRecord.PriorityNormal, null, "");
        Check(open != null, "test task 1 was not stored");
        Check(tasks.AddTask("Kupić chleb", TaskRecord.PriorityNormal, null, "") != null, "test task 2 was not stored");
        var finished = tasks.AddTask("Stary raport roczny", TaskRecord.PriorityNormal, null, "");
        Check(finished != null && tasks.SetTaskStatus(finished.Id, TaskRecord.StatusDone), "test task 3 was not closed");
        Check(tasks.AddReminder("Zadzwonić w sprawie raportu", DateTime.Now.AddHours(2), "") != null, "test reminder was not stored");
        int taskCount = tasks.GetTasks(includeDone: true).Count;

        string found = await router.ProcessAsync("szukaj w zadaniach: raport", default);
        Check(found.Contains("Wysłać raport kwartalny") && found.Contains("Stary raport roczny") && found.Contains("[zrobione]") && found.Contains("Zadzwonić w sprawie raportu"),
            "Task search must list open tasks, done tasks and reminders: " + found);
        Check(!found.Contains("chleb"), "Task search must only return matches: " + found);
        string accents = await router.ProcessAsync("szukaj w zadaniach: wysłać kwartalny", default);
        Check(accents.Contains("Wysłać raport kwartalny") && !accents.Contains("Stary raport"), "Every word of the phrase must match, diacritics included: " + accents);
        string plain = await router.ProcessAsync("szukaj w zadaniach: WYSLAC", default);
        Check(plain.Contains("Wysłać raport kwartalny"), "Task search must ignore case and diacritics: " + plain);
        string none = await router.ProcessAsync("szukaj w zadaniach: kosmos", default);
        Check(none.Contains("nie ma nic z frazą „kosmos”"), "No match must be said honestly: " + none);
        string bare = await router.ProcessAsync("szukaj w zadaniach", default);
        Check(bare.Contains("Podaj frazę"), "A bare task search must show usage, not a web search: " + bare);
        Check(tasks.GetTasks(includeDone: true).Count == taskCount, "Task search is read-only and must not change the tasks.");
        Check(tasks.Search("   ").Tasks.Count == 0 && tasks.Search("raport", limit: 1).Tasks.Count == 1, "TaskService.Search must ignore an empty phrase and respect the limit.");
        await Task.CompletedTask;
    }
}
