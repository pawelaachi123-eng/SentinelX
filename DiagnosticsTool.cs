using SentinelX.Services.Network;

namespace SentinelX;

/// <summary>Routes common performance symptoms to read-only evidence collection and cautious hypotheses.</summary>
public sealed class DiagnosticsTool
{
    private readonly ProcessToolService processes;
    private readonly INetworkService network;
    private readonly PerformanceHistoryService performance;

    public DiagnosticsTool(ProcessToolService processes, INetworkService network, PerformanceHistoryService performance)
    { this.processes = processes; this.network = network; this.performance = performance; }

    public async Task<string?> TryProcessAsync(string command, CancellationToken token = default)
    {
        string input = ConversationMemoryService.Normalize(command).Trim().TrimEnd('.', '!', '?');
        if (input is "sprawdz temperature gpu" or "pokaz temperature gpu" or "temperatura gpu" or "ile stopni gpu")
            return CurrentPerformance("Temperatura GPU nie jest dostępna: Sentinel nie ma w tej konfiguracji czujnika temperatury.");
        if (input is "sprawdz temperature cpu" or "pokaz temperature cpu" or "temperatura cpu" or "ile stopni cpu")
            return CurrentPerformance("Temperatura CPU nie jest dostępna: Sentinel nie ma w tej konfiguracji czujnika temperatury.");
        if (input is "co spowodowalo tego laga" or "co spowodowalo laga" or "dlaczego byl lag" or "sprawdz ostatnie scinki" or "co sie dzialo podczas laga")
            return "Odczyt bufora wydajności:\n" + performance.FormatRecentWindow(TimeSpan.FromMinutes(3)) +
                   "\n\nTo korelacja z krótkim buforem, nie dowód przyczyny. Sentinel nie zachowuje zrzutu na dysku.";
        if (input is "sprawdz dlaczego mam scinki" or "sprawdz dlaczego mam scinki w grze" or "cs2 mi scina" or
            "cs2 mi sie zacina" or "gra mi scina" or "gra sie zacina" or "diagnozuj scinki" or "sprawdz scinki")
            return DiagnosePerformance("Wydajność / ścinki", token);
        if (input is "co zjada ram" or "co zuzywa ram" or "najwiecej ram")
            return "Odczyt procesów — sortowanie według bieżącego Working Set, nie diagnoza przyczyny ścinek:\n" + processes.GetTopMemoryProcesses(10);
        if (input is "sprawdz dlaczego mam wysoki ping" or "dlaczego mam wysoki ping" or "sprawdz wysoki ping" or "diagnozuj ping")
        {
            var test = await network.TestInternetAsync(token).ConfigureAwait(false);
            return "Diagnostyka łączności (ping do 1.1.1.1, DNS i HTTPS):\n" + test.Message +
                   (string.IsNullOrWhiteSpace(test.Evidence) ? "" : "\n" + test.Evidence) +
                   "\n\nTo testy punktowe; nie pokazują wykorzystania łącza przez poszczególne aplikacje ani jakości trasy do serwera gry.";
        }
        return null;
    }

    private string DiagnosePerformance(string title, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var sample = performance.SampleIfDue();
        string processesText = processes.GetTopMemoryProcesses(8);
        var hypotheses = new List<string>();
        if (float.IsFinite(sample.CpuPercent) && sample.CpuPercent >= 90)
            hypotheses.Add($"Wysokie bieżące użycie CPU ({sample.CpuPercent:0}%) może ograniczać czas procesora.");
        if (double.IsFinite(sample.RamPercent) && sample.RamPercent >= 92)
            hypotheses.Add($"Mało wolnej pamięci RAM (zajęte {sample.RamPercent:0}%) może powodować doczytywanie lub presję pamięci.");
        if (float.IsFinite(sample.GpuPercent) && sample.GpuPercent >= 98)
            hypotheses.Add($"Licznik silnika GPU jest blisko maksimum ({sample.GpuPercent:0}%); ograniczenie GPU jest możliwe.");
        string hypothesisText = hypotheses.Count == 0
            ? "Brak potwierdzonej hipotezy: w dostępnej bieżącej próbce nie widać przeciążenia CPU/RAM/GPU. Nie mam podstaw, by wskazać konkretną przyczynę."
            : "Możliwe hipotezy do sprawdzenia (nie potwierdzona przyczyna):\n• " + string.Join("\n• ", hypotheses);
        return $"{title} — odczyt lokalny o {sample.CapturedAt:HH:mm:ss}\n" +
            $"Gra wykryta: {(sample.RunningGame.Length > 0 ? sample.RunningGame : "nie wykryto")}.\n" +
            $"CPU: {Format(sample.CpuPercent)}; RAM: {Format(sample.RamPercent)}" +
            (double.IsFinite(sample.UsedRamGb) && double.IsFinite(sample.TotalRamGb) ? $" ({sample.UsedRamGb:0.0}/{sample.TotalRamGb:0.0} GiB)" : "") +
            $"; GPU: {Format(sample.GpuPercent)}.\n\n{hypothesisText}\n\n{processesText}\n\n" +
            "Ograniczenia: brak odczytu temperatur, VRAM, taktowań, aktywności dysku, sterowników i frametime/FPS. Powyższe procesy pokazują RAM, nie zużycie CPU/GPU. Nie zmieniono ustawień.";
    }

    private string CurrentPerformance(string note)
    {
        var sample = performance.SampleIfDue();
        return note + "\n" + $"Bieżące: CPU {Format(sample.CpuPercent)}, RAM {Format(sample.RamPercent)}, GPU {Format(sample.GpuPercent)}.\n" +
            "Temperatura, VRAM i taktowania nie są dostępne w tym odczycie; brak danych nie oznacza 0.";
    }

    private static string Format(double value) => double.IsFinite(value) ? value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "niedostępne";
}
