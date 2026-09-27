using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 2 (drugi przyrost) — zarządzanie modelami: nazwy, postęp pobierania
/// i bramka zgody dwuetapowej. Sieć jest symulowana jako zawsze martwa (AlwaysOfflineHandler),
/// więc test jest deterministyczny na każdej maszynie — także tej z uruchomioną Ollamą, czyli
/// regresja nigdy niczego naprawdę nie pobiera i nigdy niczego naprawdę nie usuwa.</summary>
internal static class ModelManagementRegression
{
    /// <summary>Każde żądanie kończy się HttpRequestException — jak brak Ollamy na 127.0.0.1:11434.</summary>
    private sealed class AlwaysOfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("regresja: symulowany brak Ollamy"));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // ---------------- czysta logika: nazwy modeli ----------------
        Check(LocalAiService.NormalizePullModelName("  Qwen3:1.7B ") == "qwen3:1.7b", "nazwa jest normalizowana do małych liter i trimowana");
        Check(LocalAiService.NormalizePullModelName("nomic-embed-text") == "nomic-embed-text", "nazwa bez tagu jest poprawna");
        Check(LocalAiService.NormalizePullModelName("rm -rf /") is null, "nazwa ze spacją nie przechodzi");
        Check(LocalAiService.NormalizePullModelName("../../etc/passwd") is null, "ścieżka nie przechodzi jako nazwa modelu");
        Check(LocalAiService.NormalizePullModelName("") is null, "pusta nazwa nie przechodzi");
        Check(LocalAiService.NormalizePullModelName("model:tag:dodatkowy") is null, "dwa tagi nie przechodzą");

        // ---------------- czysta logika: postęp pobierania ----------------
        Check(LocalAiService.FormatBytes(4_700_000_000) == "4,7 GB", "GB z polskim przecinkiem: " + LocalAiService.FormatBytes(4_700_000_000));
        Check(LocalAiService.FormatBytes(750_000_000) == "750 MB", "MB poniżej gigabajta: " + LocalAiService.FormatBytes(750_000_000));
        string progress = LocalAiService.FormatPullProgress("qwen3:1.7b", 1_500_000_000, 4_700_000_000);
        Check(progress.Contains("31,9%") && progress.Contains("1,5 GB") && progress.Contains("4,7 GB"),
            "postęp pokazuje procent i bajty: " + progress);
        Check(LocalAiService.FormatPullProgress("m", 10, 0).Contains("nieznany"), "zerowy total nie dzieli przez zero");

        var statusOnly = LocalAiService.ParsePullProgressLine("{\"status\":\"pulling manifest\"}");
        Check(statusOnly != null && statusOnly.Value.Status == "pulling manifest" && statusOnly.Value.Total == 0, "linia tylko ze statusem");
        var withBytes = LocalAiService.ParsePullProgressLine("{\"status\":\"pulling qwen3:1.7b\",\"completed\":1500000000,\"total\":4700000000}");
        Check(withBytes != null && withBytes.Value.Completed == 1_500_000_000L && withBytes.Value.Total == 4_700_000_000L, "linia z postępem w bajtach");
        var errorLine = LocalAiService.ParsePullProgressLine("{\"error\":\"registry unreachable\"}");
        Check(errorLine != null && errorLine.Value.Error == "registry unreachable", "linia z błędem Ollamy jest rozpoznawana");
        Check(LocalAiService.ParsePullProgressLine("to nie jest json") is null, "śmieć w strumieniu jest ignorowany, nie wywala pobierania");

        // ---------------- szacunek pobierania z katalogu ----------------
        Check((ModelToolbox.DownloadEstimate("qwen2.5:7b") ?? "").Contains("4,2 GB"), "szacunek 7B w q4_K_M: " + ModelToolbox.DownloadEstimate("qwen2.5:7b"));
        Check(ModelToolbox.DownloadEstimate("model-poza-katalogiem") is null, "nieznany model: uczciwe „nie znam rozmiaru”");
        Check((ModelToolbox.DownloadEstimate("QWEN2.5:7B") ?? "").Contains("4,2 GB"), "szacunek nie zależy od wielkości liter");

        // ---------------- dobór modelu do zadania i licencje ----------------
        string code = RequireTool("model do zadania: pisanie kodu");
        Check(code.Contains("qwen2.5-coder:7b"), "kod → model kodowy: " + code.Split('\n')[0]);
        Check(RequireTool("model do zadania: opisywanie zdjęć").Contains("llava:7b"), "zdjęcia → model wizji");
        Check(RequireTool("model do zadania: embeddingi do wyszukiwania semantycznego").Contains("nomic-embed-text"), "wyszukiwanie → embeddingi");
        Check(RequireTool("model do zadania: zadania z logiki i matematyki").Contains("deepseek-r1:8b"), "rozumowanie → model rozumujący");
        Check(RequireTool("model do zadania: streszczanie dokumentów").Contains("qwen3:4b-instruct"), "streszczanie → domyślny model rozmowy");
        Check(RequireTool("model do zadania: coś szybkiego na starym laptopie").Contains("qwen3:1.7b"), "słaby sprzęt → najlżejszy model");
        Check(RequireTool("model licencje").Contains("Apache 2.0"), "licencje wymieniają Apache 2.0");
        Check(RequireTool("model licencje").Contains("nie jest prawnikiem"), "licencje uczciwie mówią, że to nie porada prawna");

        // ---------------- bramka zgody: dwa kroki, sieć martwa ----------------
        string root = Path.Combine(directory, "data");
        Directory.CreateDirectory(root);
        using var monitor = new SystemMonitor();
        using var ai = new LocalAiService(new GamingModeService(), new AlwaysOfflineHandler(), settingsDirectory: root);
        var memory = new ConversationMemoryService(Path.Combine(root, "Memory"));
        var router = new CommandRouter(monitor, new SystemInfoService(), ai, memory);

        string plan = await router.ProcessAsync("model pobierz qwen3:1.7b");
        Check(plan.Contains("PLAN POBIERANIA") && plan.Contains("potwierdzam"), "pobranie zaczyna się od planu, nie od pobierania: " + plan.Split('\n')[0]);
        Check(plan.Contains("qwen3:1.7b"), "plan wymienia dokładną nazwę");
        Check(!ai.PullInProgress, "plan nie startuje pobierania");

        string foreign = await router.ProcessAsync("model pobierz inny-model:7b potwierdzam");
        Check(foreign.Contains("innej nazwy") || foreign.Contains("nieaktualna"), "zgoda nie przechodzi na inną nazwę: " + foreign.Split('\n')[0]);
        Check(!ai.PullInProgress, "obca zgoda niczego nie uruchamia");

        string offlinePull = await router.ProcessAsync("model pobierz qwen3:1.7b potwierdzam");
        Check(offlinePull.Contains("Ollam") && offlinePull.Contains("127.0.0.1:11434"), "pobieranie bez Ollamy kończy się jawnym komunikatem: " + offlinePull.Split('\n')[0]);
        Check(!ai.PullInProgress, "po nieudanym pobieraniu flaga gaśnie");

        string reuse = await router.ProcessAsync("model pobierz qwen3:1.7b potwierdzam");
        Check(reuse.Contains("nieaktualna") || reuse.Contains("Najpierw plan"), "zgoda jest jednorazowa: " + reuse.Split('\n')[0]);

        string badName = await router.ProcessAsync("model pobierz: rm -rf x");
        Check(badName.Contains("Niepoprawna nazwa"), "śmieciowa nazwa nie idzie w sieć: " + badName.Split('\n')[0]);

        string deletePlan = await router.ProcessAsync("model usun qwen3:1.7b");
        Check(deletePlan.Contains("PLAN USUWANIA") && deletePlan.Contains("nieodwracalne"), "usuwanie zaczyna się od planu z ostrzeżeniem");
        string deleteOffline = await router.ProcessAsync("model usun qwen3:1.7b potwierdzam");
        Check(deleteOffline.Contains("Ollam"), "usuwanie bez Ollamy mówi wprost: " + deleteOffline.Split('\n')[0]);
        string deleteMismatch = await router.ProcessAsync("model usun inne-modele:2b potwierdzam");
        Check(deleteMismatch.Contains("nieaktualna") || deleteMismatch.Contains("innej nazwy"), "zgoda na usuwanie też jest związana z nazwą");

        string status = await router.ProcessAsync("model status pobierania");
        Check(status.Contains("Nie trwa żadne pobieranie"), "status pobierania bez sieci zwraca spokojną informację: " + status);

        string copyUsage = await router.ProcessAsync("model kopiuj: /etc/passwd do kopia");
        Check(copyUsage.Contains("Użycie") || copyUsage.Contains("Nazwy bez"), "ścieżka nie przechodzi jako źródło kopii: " + copyUsage.Split('\n')[0]);

        // ---------------- routing pozostałych sekcji (przez realny router) ----------------
        string arch = await router.ProcessAsync("moduly: a>b b>c c");
        Check(arch.Contains("GRAF MODUŁÓW"), "router dopina architekturę (moduly:): " + arch.Split('\n')[0]);
        string cite = await router.ProcessAsync("cytuj apa: Nowak | 2024 | Raport | Wydawnictwo");
        Check(cite.Contains("Nowak (2024)"), "router dopina research (cytuj apa): " + cite);
        string indexUsage = await router.ProcessAsync("indeks zbuduj");
        Check(indexUsage.Contains("Podaj folder"), "router dopina indeks wiedzy: " + indexUsage);
        string chart = await router.ProcessAsync("wykres: 1 2");
        Check(chart.Contains("WYKRES ZAPISANY"), "router dopina wykres (Analysis): " + chart.Split('\n')[0]);

        // ---------------- zwykłe zdania nie są poleceniami zarządzania ----------------
        // (przez router nie idą: bez Ollamy wpadłyby w ścieżkę AI, która nie jest tematem tej suity)
        foreach (string sentence in new[]
        {
            "czy pobieranie modeli jest bezpieczne", "model zarządzania czasem to mit", "usunięcie modelu z listy życzeń",
            "pobranie modelu myślowego też się liczy", "status pobierania serialu: odcinek 3",
        })
            Check(ModelToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem zarządzania modelami: " + sentence);

        File.WriteAllText(Path.Combine(directory, "model-management.txt"),
            "PASS\nNormalizePullModelName + FormatBytes + FormatPullProgress + ParsePullProgressLine verified\n" +
            "Consent gate: plan → „potwierdzam” (jednorazowa, związana z nazwą), offline message honest\n" +
            "Recommend-by-task and licenses verified (offline, no network)\n");
    }

    private static string RequireTool(string command) =>
        ModelToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nie zostało obsłużone przez ModelToolbox");
}
