using System.IO;
using System.Text.Json;

namespace SentinelX;

internal static class AiTestRunner
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        await AiReliabilityTestRunner.RunAsync(directory);

        using var service = new LocalAiService(
            new GamingModeService(),
            settingsDirectory: Path.Combine(directory, "ai-settings"));

        var models = await service.GetInstalledModelsAsync();
        if (models.Count == 0)
            throw new InvalidOperationException("Ollama działa, ale nie znaleziono modelu rozmowy z capability chat/completion.");

        string answer = await service.AskAsync("Odpowiedz po polsku jednym krótkim zdaniem: test Sentinel X.");
        if (!service.LastResponseSucceeded || string.IsNullOrWhiteSpace(answer) ||
            answer.Contains("Brak połączenia", StringComparison.OrdinalIgnoreCase) ||
            answer.Contains("Nie znaleziono lokalnego modelu", StringComparison.OrdinalIgnoreCase) ||
            answer.Contains("zgłosił błąd HTTP", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Lokalny model AI nie zwrócił użytecznej odpowiedzi: " + answer);

        string misleadingContext = "Stary tekst: użytkownik jest w pracy, ma problem z połączeniem internetowym i wymaga pomocy Microsoft.";
        string birdAnswer = await service.AskAsync("Dlaczego kruki odlatują z Polski na zimę?", misleadingContext);
        if (!service.LastResponseSucceeded || birdAnswer.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
            birdAnswer.Contains("połączeniem internetowym", StringComparison.OrdinalIgnoreCase) ||
            birdAnswer.Contains("zgodnie z podanym tekstem", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Model odpowiedział starym kontekstem zamiast aktualnym pytaniem: " + birdAnswer);

        var qwenProof = new List<object>();
        foreach (string name in new[] { "qwen3:1.7b", "qwen3:4b-instruct" }.Where(models.Contains))
        {
            await service.SetPreferredModelAsync(name);
            string probe = await service.AskAsync("Odpowiedz jednym zdaniem po polsku: do czego służy pamięć RAM?");
            qwenProof.Add(new { model = name, actualModel = service.LastModel, success = service.LastResponseSucceeded && service.LastModel == name,
                elapsedMilliseconds = (int)service.LastResponseTime.TotalMilliseconds, response = probe, fallback = service.LastFallbackReason });
        }
        File.WriteAllText(
            Path.Combine(directory, "ai-results.json"),
            JsonSerializer.Serialize(
                new
                {
                    models,
                    selectedModel = service.LastModel,
                    elapsedMilliseconds = (int)service.LastResponseTime.TotalMilliseconds,
                    responsePreview = answer.Length <= 300 ? answer : answer[..300],
                    contextRegressionPreview = birdAnswer.Length <= 300 ? birdAnswer : birdAnswer[..300],
                    qwenProof,
                    note = "Realny test lokalnego Ollama przez SentinelX LocalAiService. Bez API chmurowego."
                },
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
