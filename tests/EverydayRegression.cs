using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJE 7/11/13 (dołączone do produktywności) — rozpoznawanie języka po
/// stopwordach, braki i18n, szablon webhooka, token bucket, plan ponowień, sesje pracy,
/// koszt spotkania, godziny pracy. Liczby sprawdzone ręcznie przy każdej asercji.</summary>
internal static class EverydayRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Handle(string command) =>
        ProductivityToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nieobsłużone");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        Check(Handle("jezyk: the quick brown fox jumps over the lazy dog").Contains("angielski"), "stopwordy angielskie rozpoznane");
        Check(Handle("jezyk: Ala ma kota a kot ma Ale i bardzo jej się podoba").Contains("polski"), "stopwordy polskie rozpoznane");
        Check(Handle("i18n: pl: ok=ok; save=zapisz | en: ok=ok").Contains("save"), "brakujący klucz „save” w en");
        Check(Handle("webhook szablon: zamowienie").Contains("X-Signature"), "webhook z podpisem HMAC");

        string bucket = Handle("token bucket: 100 10");
        Check(bucket.Contains("pojemność 100, dopływ 10/s") && bucket.Contains("Retry-After"), "token bucket: burst 100, dopływ 10/s");

        string retry = Handle("retry plan: 3 30");
        Check(retry.Contains("30 s, 60 s, 120 s"), "backoff wykładniczy 30/60/120: " + retry.Split('\n')[0]);
        Check(retry.Contains("4xx"), "uczciwie: błędy żądania nie są ponawiane");

        string sessions = Handle("sesje: 4 25 5");
        Check(sessions.Contains("razem: 130 min"), "4×25 pracy + 3×5 + 15 przerwy = 130 min: " + sessions.Split('\n').Last());
        Check(Handle("koszt spotkania: 6 60 120").Contains("= 720 zł"), "6 osób × 1 h × 120 zł = 720 zł");
        Check(Handle("godziny pracy: 8:00-16:30 45").Contains("7 h 45 netto (465 min)"), "510 min minus 45 przerwy");
        string week = Handle("plan tygodnia: pn=raport; wt=testy; beda=inne");
        Check(week.Contains("· Pn: raport") && week.Contains("· Wt: testy") && week.Contains("· Śr: —"), "siatka tygodnia z pustymi dniami: " + week.Split('\n')[1]);
        Check(week.Contains("nieznane dni: 1"), "nieznany dzień liczony uczciwie");

        // istniejące polecenia produktywności nadal działają (brak regresji po doklejce)
        Check(Handle("roi: 13000 2000").Contains("650"), "ROI nadal liczone (zysk 13000 / koszt 2000 = 650%)");

        foreach (string sentence in new[] { "język polski jest trudny", "sesje filmowe uwielbiam", "koszt spotkań rośnie z każdym kwartałem" })
            Check(ProductivityToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem produktywności: " + sentence);

        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "everyday.txt"),
            "PASS\nlanguage detection, i18n gaps, webhook template, token bucket, retry plan, sessions, meeting cost, work hours verified\n");
        return Task.CompletedTask;
    }
}
