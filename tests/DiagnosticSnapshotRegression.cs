using System.IO;
using System.Linq;
using System.Text.Json;

namespace SentinelX.Tests;

/// <summary>Diagnostic snapshots: pure line-level comparison, persistence, single delete, verified export.</summary>
internal static class DiagnosticSnapshotRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // --- pure comparison on synthetic readings (no machine calls, fully deterministic) ---
        var first = new DiagnosticSnapshot
        {
            Id = "first", Label = "przed zmianą", CapturedAt = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero),
            Sections =
            [
                new DiagnosticSection("system", "Komputer i wydajność", "dostępny", "CPU: 12%\nRAM: 4.0 / 16.0 GiB\nSprzęt zgłasza stabilne zasilanie"),
                new DiagnosticSection("disks", "Dyski lokalne", "dostępny", "C: wolne 40 GiB\nPlik wymiany: włączony"),
                new DiagnosticSection("events", "Zdarzenia systemu", "niedostępny", "NIEZNANY — brak dostępu")
            ]
        };
        var second = new DiagnosticSnapshot
        {
            Id = "second", Label = "po zmianie", CapturedAt = new DateTimeOffset(2026, 9, 23, 12, 30, 0, TimeSpan.Zero),
            Sections =
            [
                new DiagnosticSection("system", "Komputer i wydajność", "dostępny", "CPU: 55%\nRAM: 4.0 / 16.0 GiB\nNowy sterownik karty sieciowej"),
                new DiagnosticSection("disks", "Dyski lokalne", "częściowy", "C: wolne 12 GiB"),
                new DiagnosticSection("network", "Interfejsy sieci", "odczyt lokalny", "Ethernet: połączony")
            ]
        };
        var diff = DiagnosticSnapshotService.Compare(first, second);
        Check(diff.TotalChanges > 0, "differences between different readings are found");
        var system = diff.Sections.Single(x => x.SectionTitle == "Komputer i wydajność");
        Check(system.Changed.Any(x => x.From.Contains("CPU: 12%") && x.To.Contains("CPU: 55%")), "a changed value is reported as from → to");
        Check(system.Added.Contains("Nowy sterownik karty sieciowej"), "an appeared line is listed");
        Check(system.Removed.Contains("Sprzęt zgłasza stabilne zasilanie"), "a disappeared line is listed");
        Check(!system.Changed.Any(x => x.From.Contains("RAM")), "an unchanged value is not reported as a change");
        var disks = diff.Sections.Single(x => x.SectionTitle == "Dyski lokalne");
        Check(disks.Changed.Any(x => x.From.Contains("stan odczytu: dostępny") && x.To.Contains("stan odczytu: częściowy")), "a section read status change is visible");
        Check(disks.Removed.Contains("Plik wymiany: włączony"), "a key missing in the second read is reported");
        Check(diff.SectionsOnlyInFirst.Contains("Zdarzenia systemu"), "a section absent from the second read is listed, never treated as equal");
        Check(diff.Sections.Any(x => x.SectionTitle.StartsWith("Interfejsy sieci")), "a section that only exists in the second read is reported");
        Check(diff.ToMarkdown().Contains("Porównanie snapshotów diagnostycznych"), "the report has a header");
        Check(diff.ToMarkdown().Contains("CPU: 12%"), "the report shows the actual changed values");

        var same = DiagnosticSnapshotService.Compare(first, new DiagnosticSnapshot
        { Id = "copy", Label = "kopia", CapturedAt = first.CapturedAt, Sections = first.Sections.ToList() });
        Check(same.TotalChanges == 0 && same.SectionsOnlyInFirst.Count == 0, "identical readings report no differences");
        Check(same.Headline.Contains("Brak różnic"), "no-difference headline is explicit");

        // --- real service: capture, persist, reload, compare, export, delete ---
        string store = Path.Combine(directory, "store");
        Directory.CreateDirectory(store);
        var service = new DiagnosticSnapshotService(directory: store);
        Check(service.GetSnapshots().Count == 0, "a fresh store has no readings");
        var capturedA = await service.CaptureAsync("smoke A");
        var capturedB = await service.CaptureAsync("smoke B");
        Check(capturedA != null && capturedB != null, "two readings were captured");
        if (capturedA == null || capturedB == null) return;
        Check(capturedA!.Sections.Count > 0, "a reading contains diagnostic sections");
        Check(service.GetSnapshots().Count == 2, "both readings are listed");

        var reloaded = new DiagnosticSnapshotService(directory: store);
        Check(reloaded.GetSnapshots().Count == 2, "readings survive a restart");
        Check(reloaded.VerifyPersistedState(out string evidence) && evidence.Contains("SHA-256"), "read-back proof with a hash");
        Check(reloaded.Compare(capturedA.Id, capturedB.Id, out _) != null, "comparison by id works after a reload");
        Check(reloaded.Compare(capturedA.Id, capturedA.Id, out string sameReason) == null && sameReason.Contains("ten sam"),
            "comparing a reading with itself is refused with an explanation");
        Check(reloaded.Compare("brak", capturedB.Id, out string missingReason) == null && missingReason.Contains("istniejące"),
            "an unknown id is refused");

        string exportDirectory = Path.Combine(directory, "export");
        var export = await reloaded.ExportComparisonAsync(capturedA.Id, capturedB.Id, directory: exportDirectory);
        Check(export.Status == "VERIFIED" && export.Evidence.Contains("SHA-256"), "export is verified with a read-back hash");
        Check(export.Evidence.Contains(".md") && export.Evidence.Contains(".json"), "export writes Markdown and JSON");
        Check(Directory.GetFiles(exportDirectory).Length == 2, "exactly two report files were written");

        Check(reloaded.Delete(capturedB.Id) && reloaded.GetSnapshots().Count == 1, "a single reading can be deleted");
        Check(!reloaded.Delete(capturedB.Id), "deleting the same reading twice is refused");
        var afterDelete = new DiagnosticSnapshotService(directory: store);
        Check(afterDelete.GetSnapshots().Count == 1, "the delete survived a restart");
        Check(reloaded.List().Contains("smoke A"), "the list names the remaining reading");

        // --- bounded store: the oldest readings are trimmed, not accumulated forever ---
        string capped = Path.Combine(directory, "capped");
        Directory.CreateDirectory(capped);
        var state = new SnapshotState
        {
            Version = 1,
            Snapshots = Enumerable.Range(0, 25).Select(i => new DiagnosticSnapshot
            { Id = "id" + i, Label = "odczyt " + i, CapturedAt = DateTimeOffset.Now.AddMinutes(i) }).ToList()
        };
        File.WriteAllText(Path.Combine(capped, "snapshots.json"), JsonSerializer.Serialize(state));
        var trimmed = new DiagnosticSnapshotService(directory: capped);
        Check(trimmed.GetSnapshots().Count == 20, "the store is trimmed to the 20-reading limit");
        Check(trimmed.GetSnapshots().All(x => x.Label != "odczyt 0"), "the oldest reading is the one dropped");

        // --- damaged store: honest error plus a preserved copy ---
        string damagedDirectory = Path.Combine(directory, "damaged");
        Directory.CreateDirectory(damagedDirectory);
        File.WriteAllText(Path.Combine(damagedDirectory, "snapshots.json"), "{ to nie jest json");
        var damaged = new DiagnosticSnapshotService(directory: damagedDirectory);
        Check(damaged.GetSnapshots().Count == 0 && damaged.LastStorageError != null, "a damaged store is reported, not silently empty");
        Check(Directory.GetFiles(damagedDirectory, "snapshots.json.damaged-*").Length == 1, "the damaged file is copied for recovery");
    }
}
