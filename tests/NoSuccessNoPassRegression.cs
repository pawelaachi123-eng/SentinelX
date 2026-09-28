using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SentinelX.Core;
using SentinelX.Models;

namespace SentinelX.Tests;

/// <summary>0.99 · ZASADA „NO SUCCESS = NO PASS” (44. zestaw).
/// Sentinel nie może ogłaszać sukcesu bez dowodu: VerificationCenter to niezależny
/// sąd nad dowodami akcji — obala VERIFIED bez dowodu, werdykt z komunikatem błędu,
/// „pomiar” z jednostką a bez liczby, niedokończone sekwencje WORKFLOW, oraz pozwala
/// każdej rodzinie narzędzi dopisać własne post-kondycje wg prefiksu typu.
/// Silnik stosuje wyrok: obalony VERIFIED staje się FAILED ze szczerym komunikatem,
/// a odpowiedź bez dowodu na polecenie-systemowe dostaje pieczęć NO SUCCESS = NO PASS.</summary>
internal static class NoSuccessNoPassRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static ActionHistoryEntry Entry(string id, string type, string status, string message, string evidence) => new()
    {
        ActionId = id,
        ActionType = type,
        Status = status,
        Message = message,
        Evidence = evidence,
        Timestamp = DateTime.Now,
    };

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // ————— 1. VERIFIED bez dowodu = obalony —————
        var noProof = new[] { Entry("SX-1", "MEASURE_RAM", "VERIFIED", "Odczytano pamięć.", "") };
        var (passed1, findings1) = VerificationCenter.Evaluate("ile mam RAM", "MEASURE_RAM", noProof);
        Check(!passed1, "VERIFIED bez dowodu musi być obalony");
        Check(findings1.Any(f => f.Contains("bez dowodu")), "powód ma nazywać rzecz: dowód");

        // ————— 2. Liczba bez pomiaru (jednostka bez cyfry) = obalony —————
        var ghostNumber = new[] { Entry("SX-2", "MEASURE_CPU", "VERIFIED", "Użycie procentowe: wysokie %", "raport") };
        var (passed2, findings2) = VerificationCenter.Evaluate("użycie CPU", "MEASURE_CPU", ghostNumber);
        Check(!passed2, "jednostka bez liczby musi być obalona");
        Check(findings2.Any(f => f.Contains("pomiar nieudowodniony")), "powód: pomiar nieudowodniony");

        // ————— 3. VERIFIED z komunikatem o błędzie = sprzeczność —————
        var mixed = new[] { Entry("SX-3", "OPEN_APP", "VERIFIED", "Wystąpił błąd podczas otwierania.", "log") };
        var (passed3, findings3) = VerificationCenter.Evaluate("uruchom kalkulator", "OPEN_APP", mixed);
        Check(!passed3, "werdykt z błędem w treści musi być obalony");
        Check(findings3.Any(f => f.Contains("z komunikatem błędu")), "powód: sprzeczny werdykt");

        // ————— 4. WORKFLOW z jednym narzędziem = sekwencja niedokończona —————
        var shortWorkflow = new[] { Entry("SX-4", "CLEANUP", "VERIFIED", "Krok 1 ok.", "dowód kroku") };
        var (passed4, findings4) = VerificationCenter.Evaluate("posprzątaj", "WORKFLOW", shortWorkflow);
        Check(!passed4, "WORKFLOW z 1 narzędziem musi być obalony");
        Check(findings4.Any(f => f.Contains("niedokończona")), "powód: sekwencja niedokończona");

        // ————— 5. Sekwencja łącząca VERIFIED i FAILED = całość nie jest sukcesem —————
        var halfDone = new[]
        {
            Entry("SX-5a", "CLEANUP", "VERIFIED", "Krok 1 ok.", "dowód kroku 1"),
            Entry("SX-5b", "CLEANUP", "FAILED", "Krok 2 nie wyszedł.", "dowód kroku 2"),
        };
        var (passed5, findings5) = VerificationCenter.Evaluate("posprzątaj", "WORKFLOW", halfDone);
        Check(!passed5, "sekwencja VERIFIED+FAILED musi być obalona");
        Check(findings5.Any(f => f.Contains("łączy VERIFIED i FAILED")), "powód: częściowa prawda");

        // ————— 6. Uczciwy, udowodniony sukces przechodzi —————
        var solid = new[]
        {
            Entry("SX-6", "MEASURE_RAM", "VERIFIED", "RAM: 62% z 16 GB", "pomiar: 62; total 16 GB; źródło: system"),
            Entry("SX-7", "MEASURE_CPU", "VERIFIED", "CPU: 18%", "pomiar: 18; źródło: licznik"),
        };
        var (passed6, _) = VerificationCenter.Evaluate("ile mam RAM i CPU", "WORKFLOW", solid);
        Check(passed6, "dowody z liczbami przechodzą (nie karzemy uczciwych)");

        // ————— 7. Rozszerzalność: własna post-kondycja wg prefiksu typu —————
        int before = VerificationCenter.RegisteredCount;
        VerificationCenter.Register("SX42_", (request, _, entries) =>
            request.Contains("must-pass") && entries.Any(x => x.Status == "FAILED")
                ? "reguła SX42: FAILED w sekwencji testowej"
                : null);
        Check(VerificationCenter.RegisteredCount == before + 1, "rejestracja dopisuje regułę");
        var custom = new[] { Entry("SX-8", "SX42_DEMO", "FAILED", "celowo", "celowo") };
        var (passed7, findings7) = VerificationCenter.Evaluate("this must-pass", "SX42_DEMO", custom);
        Check(!passed7 && findings7.Any(f => f.Contains("SX42")), "własna reguła działa na swoim prefiksie");
        var (passed8, _) = VerificationCenter.Evaluate("this must-pass", "INNY_TYP", custom);
        Check(passed8, "własna reguła nie sięga poza swój prefiks");

        // ————— 8. Silnik: wyrok ma zdjąć status i dać szczery komunikat —————
        string engine = File.ReadAllText(Path.Combine(FindRoot(), "Services", "Actions", "ActionEngine.cs"));
        Check(engine.Contains("VerificationCenter.Evaluate"), "silnik wzywa centrum weryfikacji");
        Check(engine.Count(x => x == 'N') > 0 && engine.Contains("NO SUCCESS = NO PASS"),
            "silnik stempluje szczery komunikat");
        Check(engine.Contains("ActionStatus.Unverified => \"NO SUCCESS = NO PASS"),
            "faza UNVERIFIED mówi wprost: brak dowodu");
        Check(engine.Contains("record.Error = \"NO SUCCESS = NO PASS · \" + string.Join"),
            "obalony VERIFIED staje się FAILED");

        string center = File.ReadAllText(Path.Combine(FindRoot(), "Core", "Verification", "VerificationCenter.cs"));
        foreach (var rule in new[] { "WERDYKT BEZ DOWODU", "LICZBA BEZ POMIARU", "PRZEKONYWAJĄCY WERDYKT",
            "SEKWENCJA NIEDOKOŃCZONA", "CZĘŚCIOWA PRAWDA" })
            Check(center.Contains(rule), "brak reguły domyślnej: " + rule);

        // ————— 9. NAMYSŁ: ponawiamy tylko przyczyny chwilowe, i to RAZ —————
        Check(RetryAdvisor.TransientCause("Połączenie z usługą przerwane: timeout operacji") == "limit czasu",
            "timeout = przyczyna chwilowa (limit czasu)");
        Check(RetryAdvisor.TransientCause("Plik jest używany przez inny proces (zajęty)") == "zasób zajęty",
            "zasób zajęty = chwilowa");
        Check(RetryAdvisor.TransientCause("Błąd sieci: połączenie zerwane") == "sieć", "sieć = chwilowa");
        Check(RetryAdvisor.TransientCause("Usługa chwilowo niedostępna, spróbuj później") != null,
            "usługa niedostępna = chwilowa");
        Check(RetryAdvisor.TransientCause("Plik nie istnieje: config.json") is null,
            "brak pliku = TRWAŁA, nie ponawiamy");
        Check(RetryAdvisor.TransientCause("Odmowa dostępu do rejestru") is null,
            "odmowa dostępu = TRWAŁA, nie ponawiamy");
        Check(RetryAdvisor.TransientCause("Nieznanay błąd xyzq") is null,
            "nieznana przyczyna = nie ryzykujemy ponowienia");
        Check(engine.Contains("RetryAdvisor.TransientCause"), "silnik konsultuje doradcę ponowień");
        Check(engine.Contains("PRÓBA 1") && engine.Contains("ponawiam raz"), "ślad namysłu trafia do dowodów");

        // ————— 10. ZAUFANIE: procent z historii; pusta historia NIE daje 100% —————
        var mixedHistory = new[]
        {
            Entry("H1", "MEASURE_RAM", "VERIFIED", "RAM 62%", "pomiar: 62"),
            Entry("H2", "MEASURE_CPU", "VERIFIED", "CPU 18%", "pomiar: 18"),
            Entry("H3", "OPEN_APP", "UNVERIFIED", "tekst", "brak dowodu"),
            Entry("H4", "CLEANUP", "FAILED", "błąd", "dowód błędu"),
            Entry("H5", "RESPONSE", "RUNNING", "w toku", "—"),
        };
        var trust = TrustSummary.Calculate(mixedHistory);
        Check(trust.HasData && trust.Total == 4, "RUNNING nie jest zakończony (4 zakończone)");
        Check(trust.Verified == 2 && trust.Percent == 50, "50% VERIFIED z 4 zakończonych");
        Check(trust.Unverified == 1 && trust.Failed == 1, "bez dowodu i błędy liczone wprost");
        var emptyTrust = TrustSummary.Calculate([]);
        Check(!emptyTrust.HasData && emptyTrust.Percent == 0, "pusta historia = brak danych, NIE 100%");
        string readiness = File.ReadAllText(Path.Combine(FindRoot(), "Services", "Readiness", "ReadinessService.cs"));
        Check(readiness.Contains("TrustSummary.Calculate") && readiness.Contains("\"trust\""),
            "panel gotowości ma kartę ZAUFANIE");
        string smokeRunner = File.ReadAllText(Path.Combine(FindRoot(), "UiSmokeTestRunner.cs"));
        Check(smokeRunner.Contains("Checks.Count != 5"), "smoke pilnuje 5 kart gotowości");

        File.WriteAllText(Path.Combine(directory, "no-success-no-pass.txt"),
            "PASS\nNo success = no pass: VerificationCenter (5 reguł domyślnych + rejestr wg prefiksu) obala\n" +
            "VERIFIED bez dowodu, liczbę bez pomiaru, sprzeczne werdykty i niedokończone sekwencje;\n" +
            "silnik stosuje wyrok (VERIFIED→FAILED + szczery komunikat), UNVERIFIED na polecenie-\n" +
            "systemowe dostaje pieczęć, a PLAN→CHECK trafia do dowodów. NAMYSŁ: auto-retry raz, tylko\n" +
            "przyczyny chwilowe i nic niewykonane; ZAUFANIE: procent VERIFIED w historii, pusta = brak\n" +
            "danych; panel gotowości pokazuje 5. kartę.\n");
        return Task.CompletedTask;
    }

    private static string FindRoot()
    {
        var current = Directory.GetCurrentDirectory();
        for (int depth = 0; depth < 12 && current != null; depth++)
        {
            if (Directory.Exists(Path.Combine(current, "Services", "Actions"))) return current;
            current = Path.GetDirectoryName(current);
        }
        return AppContext.BaseDirectory;
    }
}
