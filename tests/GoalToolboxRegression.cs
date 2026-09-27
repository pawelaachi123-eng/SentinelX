using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 20 — agentic: rozkład celu, walidacja planu, budżet czasu, plan wycofania,
/// polityka autonomii, klasyfikacja ryzyka, samoocena, SMART. Szablony z twardymi regułami,
/// klasyfikacja ryzyka po czasownikach — wszystko do sprawdzenia na oczach.</summary>
internal static class GoalToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Handle(string command) =>
        GoalToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nieobsłużone");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        string goal = Handle("cel rozloz: nauczyć się gitary");
        Check(goal.Contains("CEL: nauczyć się gitary") && goal.Contains("pierwszy krok"), "cel rozłożony na kroki");

        Check(Handle("plan krokow: napisz kod; wydaj wersję").Contains("UWAGA"), "plan bez weryfikacji dostaje uwagę");
        Check(Handle("plan krokow: napisz kod; przetestuj kod; wydaj wersję").Contains("weryfikujący jest"), "plan z testem przechodzi");

        string budget = Handle("czas na zadanie: 4 60");
        Check(budget.Contains("= 240 min") && budget.Contains("300 min"), "240 min + 25% bufor = 300 min: " + budget.Split('\n')[0]);

        Check(Handle("plan wycofania: migracja bazy").Contains("kopia zapasowa"), "plan wycofania zaczyna od kopii");
        string autonomy = Handle("polityka autonomii");
        Check(autonomy.Contains("dwuetapowa"), "polityka mówi o zgodzie dwuetapowej dla destrukcyjnych");

        Check(Handle("ryzyko: usunąć plik z dysku").Contains("DESTRUKCYJNE"), "usunięcie = destrukcyjne");
        Check(Handle("ryzyko: odczytać raport").Contains("ODCZYT"), "odczyt = odczyt");
        Check(Handle("ryzyko: zmienić konfigurację").Contains("ŚREDNIE"), "zmiana = średnie");

        Check(Handle("samoocena: raport | tabela i wnioski").Contains("SAMOOCENA: raport"), "karta samooceny");
        Check(Handle("definicja sukcesu: posprzątać archiwum").Contains("SMART"), "definicja sukcesu SMART");

        foreach (string sentence in new[] { "cele na nowy rok", "ryzykowne przedsięwzięcie to jego specjalność", "plan na wakacje wygląda świetnie" })
            Check(GoalToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem celów: " + sentence);

        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "goals.txt"),
            "PASS\ngoal decomposition, plan validation, time budget, rollback, autonomy policy, risk classes, SMART verified\n");
        return Task.CompletedTask;
    }
}
