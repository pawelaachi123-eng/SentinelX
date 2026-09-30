namespace SentinelX;

/// <summary>Resolves natural references to files observed completing in Downloads during this Sentinel session.</summary>
public sealed class DownloadAssistantTool
{
    private readonly DownloadContextService downloads;
    private readonly FileWorkspaceService files;

    public DownloadAssistantTool(DownloadContextService downloads, FileWorkspaceService files)
    { this.downloads = downloads; this.files = files; }

    public async Task<string?> TryProcessAsync(string command, CancellationToken token = default)
    {
        string input = ConversationMemoryService.Normalize(command).Replace(",", "", StringComparison.Ordinal).Trim().TrimEnd('.', '!', '?');
        bool ask = input is "co przed chwila pobralem" or "jaki plik przed chwila pobralem" or "pokaz ostatnio pobrany plik";
        bool move = input is "przenies ten instalator do folderu sentinel" or "przenies to do folderu sentinel" or
            "przenies ostatnio pobrany plik do folderu sentinel";
        bool open = input is "otworz to co przed chwila pobralem" or "otworz to co pobralem" or "otworz ostatnio pobrany plik";
        if (!ask && !move && !open) return null;

        RecentDownload? recent = downloads.GetMostRecent();
        if (recent == null)
            return "Nie wykryłem w tej sesji ukończonego pobrania w folderze Pobrane. Nie wybiorę starszego pliku na chybił trafił.";
        if (!files.TrySelectContextFile(recent.Path, out string error))
            return "Wykryty plik nie jest dostępny w dozwolonym zakresie Sentinel: " + error;
        string header = $"Ostatni stabilny plik wykryty w Pobranych o {recent.CompletedAt:HH:mm}: {recent.Path}\n";
        if (ask) return header + "To plik zaobserwowany podczas tej sesji; nie sprawdzałem jego zawartości ani bezpieczeństwa.";

        token.ThrowIfCancellationRequested();
        string? response = move
            ? await files.ProcessAsync("przenieś go do folderu Sentinel", token)
            : await files.ProcessAsync("otwórz go", token);
        return header + (response ?? "Nie rozpoznałem bezpiecznej operacji na tym pliku.");
    }
}
