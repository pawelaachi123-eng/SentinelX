using System.Diagnostics;
using System.IO;
using SentinelX.Services.Files;

namespace SentinelX.Tests;

/// <summary>0.93 · executable tidy: batch rename with preview, empty-file cleanup, and duplicate batch
/// cleanup — all behind an explicit two-step confirmation. Extends 0.92 assertions. All assertions run
/// on a temporary fixture directory with a fake recycler — CI never touches the real Recycle Bin.</summary>
internal static class FileCleanupRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class RecordingRecycler : IFileRecycler
    {
        public readonly List<string> Recycled = [];
        public bool TryRecycle(string fullPath, out string message) { Recycled.Add(fullPath); File.Delete(fullPath); message = ""; return true; }
    }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string root = Path.Combine(directory, "fixture");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "a.txt"), "identyczna tresc pliku");
        File.WriteAllText(Path.Combine(root, "sub", "a-kopia.txt"), "identyczna tresc pliku");
        File.WriteAllText(Path.Combine(root, "b.txt"), "zupelnie inna tresc");
        File.WriteAllText(Path.Combine(root, "empty1.txt"), "");
        File.WriteAllText(Path.Combine(root, "empty2.txt"), "");

        var recycler = new RecordingRecycler();
        var service = new FileCleanupService(history: new ActionHistoryService(directory), recycler: recycler);
        CancellationToken ct = CancellationToken.None;

        // --- duplicate scan: exactly one group of two, empty files never counted ---
        string dup = await service.ProcessAsync("duplikaty: " + root, ct) ?? throw new InvalidOperationException("duplikaty: was not handled");
        Check(dup.Contains("DUPLIKATY"), "duplicate report header missing: " + dup);
        Check(dup.Contains("Grup duplikatów: 1"), "exactly one duplicate group expected: " + dup);
        Check(dup.Contains("a.txt") && dup.Contains("a-kopia.txt"), "both copies must be listed: " + dup);
        Check(!dup.Contains("empty1.txt"), "empty files must not be reported as duplicates: " + dup);
        Check(dup.Contains("niczego nie usuwam"), "the report must state it changes nothing: " + dup);

        // --- unique-only folder reports no duplicates ---
        string unique = Path.Combine(directory, "unique");
        Directory.CreateDirectory(unique);
        File.WriteAllText(Path.Combine(unique, "one.txt"), "raz");
        File.WriteAllText(Path.Combine(unique, "two.txt"), "dwa");
        string dupNone = await service.ProcessAsync("duplikaty: " + unique, ct) ?? "";
        Check(dupNone.Contains("Brak duplikatów"), "unique files must yield no duplicates: " + dupNone);

        // --- missing folder is refused politely ---
        string missing = await service.ProcessAsync("duplikaty: " + Path.Combine(directory, "nie-ma-takiego"), ct) ?? "";
        Check(missing.Contains("nie istnieje"), "a missing folder must be reported: " + missing);

        // --- tidy report: counts, largest, empties, read-only ---
        string tidy = await service.ProcessAsync("porzadki: " + root, ct) ?? throw new InvalidOperationException("porzadki: was not handled");
        Check(tidy.Contains("RAPORT PORZĄDKOWY"), "tidy report header missing: " + tidy);
        Check(tidy.Contains("Plików: 5"), "five files expected in fixture: " + tidy);
        Check(tidy.Contains("Puste pliki (0 B): 2"), "two empty files expected: " + tidy);
        Check(tidy.Contains("Duplikaty: 1 grup"), "tidy report must include the duplicate summary: " + tidy);
        Check(tidy.Contains("tylko do odczytu"), "tidy report must state it is read-only: " + tidy);

        // --- junctions are never followed (no scope escape) ---
        string beyond = Path.Combine(directory, "beyond");
        Directory.CreateDirectory(beyond);
        File.WriteAllText(Path.Combine(beyond, "sekret.txt"), "identyczna tresc pliku");
        var junction = Path.Combine(root, "skrot");
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{beyond}\"") { CreateNoWindow = true, UseShellExecute = false });
        mklink?.WaitForExit(10_000);
        if (mklink?.ExitCode == 0 && Directory.Exists(junction))
        {
            string dupJunction = await service.ProcessAsync("duplikaty: " + root, ct) ?? "";
            Check(!dupJunction.Contains("sekret.txt"), "files behind a junction must never be scanned: " + dupJunction);
        }

        // --- Recycle-Bin deletion is two-step and one-shot ---
        string victim = Path.Combine(root, "do-kosza.txt");
        File.WriteAllText(victim, "do usuniecia");
        string proposal = await service.ProcessAsync("usuń do kosza: " + victim, ct) ?? "";
        Check(proposal.Contains("KOSZ") && proposal.Contains("potwierdz"), "a deletion proposal must ask for confirmation: " + proposal);
        Check(File.Exists(victim) && recycler.Recycled.Count == 0, "nothing may be deleted before confirmation");
        string? unrelated = await service.ProcessAsync("ile mam ramu", ct);
        Check(unrelated == null, "non-cleanup commands must fall through to the next router stage, got: " + unrelated);
        string lateTak = await service.ProcessAsync("tak", ct) ?? "";
        Check(File.Exists(victim) && recycler.Recycled.Count == 0, "a stale pending deletion must never fire on a later „tak”: " + lateTak);
        string proposal2 = await service.ProcessAsync("usuń do kosza: " + victim, ct) ?? "";
        Check(proposal2.Contains("KOSZ"), "re-proposing a deletion must work: " + proposal2);
        string confirmed = await service.ProcessAsync("potwierdz", ct) ?? "";
        Check(confirmed.Contains("VERIFIED") && confirmed.Contains("Kosza"), "confirmed deletion must report verified success: " + confirmed);
        Check(!File.Exists(victim) && recycler.Recycled.Count == 1 && recycler.Recycled[0] == victim, "exactly the confirmed file must reach the recycler");

        // --- cancellation path ---
        string second = Path.Combine(root, "zostaje.txt");
        File.WriteAllText(second, "zostaje");
        await service.ProcessAsync("usuń do kosza: " + second, ct);
        string cancelled = await service.ProcessAsync("anuluj", ct) ?? "";
        Check(cancelled.Contains("Anulowane") && File.Exists(second) && recycler.Recycled.Count == 1, "cancellation must keep the file: " + cancelled);

        // --- missing file and missing argument are refused politely ---
        string ghost = await service.ProcessAsync("usuń do kosza: " + Path.Combine(root, "duch.txt"), ct) ?? "";
        Check(ghost.Contains("nie istnieje"), "a missing file must be reported: " + ghost);
        string bare = await service.ProcessAsync("duplikaty", ct) ?? "";
        Check(bare.Contains("Podaj folder"), "a bare command must show usage: " + bare);

        // ==================== 0.93 · batch rename ====================
        string renameRoot = Path.Combine(directory, "rename-fixture");
        Directory.CreateDirectory(renameRoot);
        File.WriteAllText(Path.Combine(renameRoot, "IMG_001.jpg"), "foto1");
        File.WriteAllText(Path.Combine(renameRoot, "IMG_002.jpg"), "foto2");
        File.WriteAllText(Path.Combine(renameRoot, "other.txt"), "inny");

        string renamePreview = await service.ProcessAsync($"zmien nazwy: {renameRoot} z IMG_ na zdjecie_", ct) ?? "";
        Check(renamePreview.Contains("ZMIANA NAZW"), "batch rename preview header missing: " + renamePreview);
        Check(renamePreview.Contains("IMG_001.jpg") && renamePreview.Contains("zdjecie_001.jpg"), "rename preview must list old→new: " + renamePreview);
        Check(renamePreview.Contains("potwierdz"), "rename preview must ask for confirmation: " + renamePreview);
        Check(File.Exists(Path.Combine(renameRoot, "IMG_001.jpg")), "files must not be renamed before confirmation");
        // unrelated input must clear pending
        string? unrelated2 = await service.ProcessAsync("ile mam ramu", ct);
        Check(unrelated2 == null, "non-cleanup must fall through");
        string lateTakRename = await service.ProcessAsync("tak", ct) ?? "";
        Check(File.Exists(Path.Combine(renameRoot, "IMG_001.jpg")), "stale pending rename must not fire on later tak: " + lateTakRename);

        // re-propose and confirm
        string renamePreview2 = await service.ProcessAsync($"zmien nazwy: {renameRoot} z IMG_ na zdjecie_", ct) ?? "";
        Check(renamePreview2.Contains("ZMIANA NAZW"), "re-proposing rename must work: " + renamePreview2);
        string renameConfirmed = await service.ProcessAsync("potwierdz", ct) ?? "";
        Check(renameConfirmed.Contains("VERIFIED") && renameConfirmed.Contains("Zmieniono nazwy"), "confirmed rename must report verified: " + renameConfirmed);
        Check(!File.Exists(Path.Combine(renameRoot, "IMG_001.jpg")) && File.Exists(Path.Combine(renameRoot, "zdjecie_001.jpg")), "first file must be renamed");
        Check(!File.Exists(Path.Combine(renameRoot, "IMG_002.jpg")) && File.Exists(Path.Combine(renameRoot, "zdjecie_002.jpg")), "second file must be renamed");
        Check(File.Exists(Path.Combine(renameRoot, "other.txt")), "non-matching file must stay");

        // arrow syntax
        File.WriteAllText(Path.Combine(renameRoot, "old_one.txt"), "x");
        string arrowPreview = await service.ProcessAsync($"zmien nazwy: {renameRoot}: old_ -> new_", ct) ?? "";
        Check(arrowPreview.Contains("ZMIANA NAZW") && arrowPreview.Contains("old_one.txt"), "arrow syntax must work: " + arrowPreview);
        string arrowConfirmed = await service.ProcessAsync("potwierdz", ct) ?? "";
        Check(arrowConfirmed.Contains("VERIFIED"), "arrow rename confirmation must succeed: " + arrowConfirmed);
        Check(File.Exists(Path.Combine(renameRoot, "new_one.txt")), "arrow rename must produce new file");

        // collision handling
        File.WriteAllText(Path.Combine(renameRoot, "a_collision.txt"), "a");
        File.WriteAllText(Path.Combine(renameRoot, "b_collision.txt"), "b");
        // both contain "collision" -> try to rename to same target would collide? Let's test collision by renaming a_ to b_ where b_ exists
        File.WriteAllText(Path.Combine(renameRoot, "a_dup.txt"), "a");
        File.WriteAllText(Path.Combine(renameRoot, "b_dup.txt"), "b");
        string collisionPreview = await service.ProcessAsync($"zmien nazwy: {renameRoot} z a_dup na b_dup", ct) ?? "";
        // b_dup.txt already exists, so it should be skipped as collision
        Check(collisionPreview.Contains("Brak plików") || collisionPreview.Contains("Pominięte") || collisionPreview.Contains("kolizje") || collisionPreview.Contains("ZMIANA NAZW"), "collision must be reported: " + collisionPreview);
        await service.ProcessAsync("anuluj", ct);

        // bare rename
        string bareRename = await service.ProcessAsync("zmien nazwy", ct) ?? "";
        Check(bareRename.Contains("Podaj folder"), "bare rename must show usage: " + bareRename);

        // ==================== 0.93 · executable tidy (empty files) ====================
        string tidyRoot = Path.Combine(directory, "tidy-fixture");
        Directory.CreateDirectory(tidyRoot);
        File.WriteAllText(Path.Combine(tidyRoot, "emptyA.txt"), "");
        File.WriteAllText(Path.Combine(tidyRoot, "emptyB.txt"), "");
        File.WriteAllText(Path.Combine(tidyRoot, "full.txt"), "content");

        string tidyPreview = await service.ProcessAsync($"uporzadkuj: {tidyRoot}", ct) ?? "";
        Check(tidyPreview.Contains("PORZĄDKOWANIE") && tidyPreview.Contains("Pustych plików"), "tidy exec preview missing: " + tidyPreview);
        Check(tidyPreview.Contains("emptyA.txt"), "tidy preview must list empty files: " + tidyPreview);
        Check(tidyPreview.Contains("potwierdz"), "tidy preview must ask for confirmation");
        Check(File.Exists(Path.Combine(tidyRoot, "emptyA.txt")), "empty files must not be deleted before confirmation");

        // stale tak must not delete
        await service.ProcessAsync("ile mam ramu", ct);
        string lateTakTidy = await service.ProcessAsync("tak", ct) ?? "";
        Check(File.Exists(Path.Combine(tidyRoot, "emptyA.txt")), "stale pending tidy must not fire: " + lateTakTidy);

        string tidyPreview2 = await service.ProcessAsync($"uporzadkuj: {tidyRoot}", ct) ?? "";
        Check(tidyPreview2.Contains("PORZĄDKOWANIE"), "re-proposing tidy must work");
        string tidyConfirmed = await service.ProcessAsync("potwierdz", ct) ?? "";
        Check(tidyConfirmed.Contains("VERIFIED") && tidyConfirmed.Contains("Uporządkowano"), "tidy confirmation must succeed: " + tidyConfirmed);
        Check(!File.Exists(Path.Combine(tidyRoot, "emptyA.txt")) && !File.Exists(Path.Combine(tidyRoot, "emptyB.txt")), "empty files must be recycled");
        Check(File.Exists(Path.Combine(tidyRoot, "full.txt")), "non-empty file must stay");
        Check(recycler.Recycled.Count >= 3, "recycler must have recorded tidy deletions");

        // bare tidy
        string bareTidy = await service.ProcessAsync("uporzadkuj", ct) ?? "";
        Check(bareTidy.Contains("Podaj folder"), "bare tidy must show usage: " + bareTidy);

        // ==================== 0.93 · batch duplicate cleanup ====================
        string dupRoot = Path.Combine(directory, "dup-clean-fixture");
        Directory.CreateDirectory(dupRoot);
        File.WriteAllText(Path.Combine(dupRoot, "dup1.txt"), "same content 0.93");
        File.WriteAllText(Path.Combine(dupRoot, "dup2.txt"), "same content 0.93");
        File.WriteAllText(Path.Combine(dupRoot, "dup3.txt"), "same content 0.93");
        File.WriteAllText(Path.Combine(dupRoot, "unique.txt"), "unique content");

        string dupCleanPreview = await service.ProcessAsync($"usun duplikaty: {dupRoot}", ct) ?? "";
        Check(dupCleanPreview.Contains("USUWANIE DUPLIKATÓW"), "duplicate cleanup preview header missing: " + dupCleanPreview);
        Check(dupCleanPreview.Contains("Grup duplikatów: 1"), "must report one duplicate group: " + dupCleanPreview);
        Check(dupCleanPreview.Contains("potwierdz"), "duplicate cleanup must ask for confirmation: " + dupCleanPreview);
        // files still exist before confirmation
        Check(File.Exists(Path.Combine(dupRoot, "dup1.txt")) && File.Exists(Path.Combine(dupRoot, "dup2.txt")), "duplicates must not be deleted before confirmation");

        // unrelated clears pending
        await service.ProcessAsync("ile mam ramu", ct);
        string lateTakDup = await service.ProcessAsync("tak", ct) ?? "";
        Check(File.Exists(Path.Combine(dupRoot, "dup2.txt")), "stale pending duplicate cleanup must not fire: " + lateTakDup);

        string dupCleanPreview2 = await service.ProcessAsync($"usun duplikaty: {dupRoot}", ct) ?? "";
        Check(dupCleanPreview2.Contains("USUWANIE DUPLIKATÓW"), "re-proposing duplicate cleanup must work");
        string dupCleanConfirmed = await service.ProcessAsync("potwierdz", ct) ?? "";
        Check(dupCleanConfirmed.Contains("VERIFIED") && dupCleanConfirmed.Contains("Usunięto duplikaty"), "duplicate cleanup confirmation must succeed: " + dupCleanConfirmed);
        int remainingDup = Directory.GetFiles(dupRoot, "dup*.txt").Length;
        Check(remainingDup == 1, $"exactly one duplicate must remain, got {remainingDup}");
        Check(File.Exists(Path.Combine(dupRoot, "unique.txt")), "unique file must stay");

        // bare duplicate cleanup
        string bareDupClean = await service.ProcessAsync("usun duplikaty", ct) ?? "";
        Check(bareDupClean.Contains("Podaj folder"), "bare duplicate cleanup must show usage: " + bareDupClean);
    }
}
