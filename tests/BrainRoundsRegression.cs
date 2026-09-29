using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SentinelX.Core;
using SentinelX.Models;

namespace SentinelX.Tests;

/// <summary>0.99 · MÓZG — 10 rund ulepszeń (45. zestaw).
/// R1 Rada naprawy (wykonywalna, per rodzina) · R2 Ślad rozumowania w rekordzie ·
/// R3 Reguły rodzin narzędzi w sądzie · R4 Księga zdrowia silnika (liczniki sesji,
/// procent zaufania, TOP typy bez dowodu, backoff) · R5 Karta zaufania łączy księgę
/// i historię + TOP typy · R6 Toast błędu z radą · R7 Pasek statusu z procentem ·
/// R8 Smoke v2 na żywo · R9 Dokument mózgu · R10 ten zestaw.</summary>
internal static class BrainRoundsRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string FindRoot()
    {
        var current = Directory.GetCurrentDirectory();
        for (int depth = 0; depth < 12 && current != null; depth++)
        {
            if (File.Exists(Path.Combine(current, "SentinelX.csproj"))) return current;
            current = Path.GetDirectoryName(current);
        }
        return AppContext.BaseDirectory;
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([FindRoot(), .. parts]));

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // ————— R1: RADA NAPRAWY — wykonywalna, per rodzina, nigdy pusta —————
        Check(RecoveryAdvisor.Advise("OPEN_APP", "nie znaleziono aplikacji").Contains("Sprawdź, czy aplikacja istnieje"),
            "OPEN_*: rada ma prowadzić przez Start/rozpoznawanie");
        Check(RecoveryAdvisor.Advise("MEASURE_RAM", "odczyt pusty").Contains("Ponów odczyt wprost"),
            "MEASURE_*: rada nakazuje ponowny mierzalny odczyt");
        Check(RecoveryAdvisor.Advise("CLEANUP", "coś poszło nie tak").Contains("Ustawienia → System → Pamięć"),
            "CLEANUP: rada wskazuje właściwe miejsce w systemie");
        Check(RecoveryAdvisor.Advise("UNKNOWN_TYPE", "timeout operacji").Contains("chwilową"),
            "rada korzysta z klasyfikacji chwilowości");
        Check(RecoveryAdvisor.Advise("UNKNOWN_TYPE", "losowy błąd").Length > 20,
            "nawet nieznana rodzina dostaje konkretną radę, nie pustkę");

        // ————— R4a: BACKOFF — rosnący, z sufitem —————
        Check(RetryAdvisor.BackoffMilliseconds(1) == 350, "backoff 1. próby = 350 ms");
        Check(RetryAdvisor.BackoffMilliseconds(2) > RetryAdvisor.BackoffMilliseconds(1), "backoff rośnie");
        Check(RetryAdvisor.BackoffMilliseconds(9) == 1200, "backoff ma sufit 1200 ms");

        // ————— R4b: KSIĘGA ZDROWIA — liczniki, procent, TOP typy —————
        var ledger = new EngineLedger();
        Check(!ledger.HasData && ledger.TrustPercent == 0, "pusta księga nie udaje zaufania");
        ledger.Record(ActionStatus.Verified, "MEASURE_RAM", false, false);
        ledger.Record(ActionStatus.Verified, "MEASURE_CPU", false, false);
        ledger.Record(ActionStatus.Verified, "MEASURE_CPU", true, false);
        ledger.Record(ActionStatus.Unverified, "OPEN_APP", false, false);
        ledger.Record(ActionStatus.Unverified, "OPEN_APP", false, false);
        ledger.Record(ActionStatus.Unverified, "CLEANUP", false, false);
        ledger.Record(ActionStatus.Failed, "NETWORK_PING", false, true);
        ledger.Record(ActionStatus.Cancelled, "CLOSE_APP", false, false);
        Check(ledger.Executed == 8 && ledger.Verified == 3 && ledger.Unverified == 3 && ledger.Failed == 1
            && ledger.Cancelled == 1 && ledger.Retried == 1 && ledger.Downgraded == 1,
            "liczniki księgi mają się zgadzać co do sztuki");
        Check(ledger.HasData && ledger.TrustPercent == 43, "procent zaufania: 3 VERIFIED / 7 zakończonych (3+3+1) = 43%");
        var top = ledger.TopUnverifiedTypes(2);
        Check(top.Count == 2 && top[0].StartsWith("OPEN_APP", StringComparison.Ordinal) && top[0].Contains("×2"),
            "TOP typy bez dowodu sortują malejąco z licznikiem ×N");

        // ————— R3: REGUŁY RODZIN w sądzie (kotwice źródłowe + zachowanie) —————
        string center = Read("Core", "Verification", "VerificationCenter.cs");
        foreach (var rule in new[] { "POMIAR BEZ LICZBY W DOWODZIE", "DOWÓD ZBYT UBOGI", "SPRZĄTANIE BEZ LICZBY" })
            Check(center.Contains(rule), "brak reguły rodzinnej: " + rule);
        string counter = Read("Core", "Verification", "EngineLedger.cs");
        Check(counter.Contains("TopUnverifiedTypes") && counter.Contains("TrustPercent"),
            "księga udostępnia procent i TOP typy");

        // ————— R1/R2/R4c: SILNIK — ślad, rada, księga, backoff (kotwice źródłowe) —————
        string engine = Read("Services", "Actions", "ActionEngine.cs");
        Check(engine.Contains("record.ReasoningTrace"), "silnik zapisuje ślad rozumowania w rekordzie");
        Check(engine.Contains("RecoveryAdvisor.Advise"), "silnik przypisuje radę naprawy po porażce");
        Check(engine.Contains("BackoffMilliseconds"), "silnik używa rosnącego backoffu");
        Check(engine.Contains("ledger.Record"), "silnik księguje wyniki w EngineLedger");
        Check(engine.Contains("NAPRAWA"), "ślad zawiera krok NAPRAWA przy obaleniu");

        // ————— R5: PANEL GOTOWOŚCI — księga + TOP typy w karcie —————
        string readiness = Read("Services", "Readiness", "ReadinessService.cs");
        Check(readiness.Contains("EngineLedger") && readiness.Contains("TopUnverifiedTypes"),
            "karta zaufania łączy księgę sesji i TOP typy bez dowodu");
        Check(readiness.Contains("najczęściej bez dowodu"), "detal karty nazywa winowajców wprost");

        // ————— R6/R7: UI — toast błędu z radą + pasek statusu z procentem —————
        string mainVm = Read("ViewModels", "MainViewModel.cs");
        Check(mainVm.Contains("TrustPercent") && mainVm.Contains("ledger.Changed"),
            "UI trzyma procent zaufania na żywo z księgi");
        Check(mainVm.Contains("RecoveryAdvice") && mainVm.Contains("ToastKind.Error"),
            "porażka akcji wyzwala toast z radą naprawy");
        string shell = Read("Views", "MainWindow.xaml");
        Check(shell.Contains("Zaufanie: {0}"), "pasek statusu pokazuje procent zaufania");

        // ————— R2: MODEL — nowe pola obserwowalne —————
        string record = Read("Models", "ActionRecord.cs");
        Check(record.Contains("recoveryAdvice") && record.Contains("reasoningTrace"),
            "ActionRecord ma pola RecoveryAdvice i ReasoningTrace");

        // ————— R9: DOKUMENT MÓZGU — istnieje i opisuje cykl —————
        string brain = Read("docs", "MOZG-SENTINELA.md");
        foreach (var marker in new[] { "PLAN → PRÓBA → CHECK → NAPRAWA → WERDYKT", "NO SUCCESS = NO PASS",
            "NAMYSŁ", "RADA NAPRAWY", "ZAUFANIE", "SZCZEROŚĆ", "JAK ROZSZERZAĆ MÓZG" })
            Check(brain.Contains(marker), "MOZG-SENTINELA.md: brak sekcji " + marker);

        File.WriteAllText(Path.Combine(directory, "brain-rounds.txt"),
            "PASS\nMózg po 10 rundach: Rada naprawy (wykonywalna, per rodzina) · ReasoningTrace (PLAN/PRÓBA/\n" +
            "CHECK/NAPRAWA w rekordzie i dowodach) · 3 reguły rodzin w sądzie · EngineLedger (sesja:\n" +
            "liczniki, % zaufania, TOP typy bez dowodu, backoff 350→1200) · karta zaufania łączy księgę\n" +
            "i historię · toast błędu z radą · pasek statusu z procentem · dokument MOZG-SENTINELA.md.\n");
        return Task.CompletedTask;
    }
}
