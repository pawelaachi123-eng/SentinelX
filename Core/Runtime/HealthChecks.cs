using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SentinelX.Core.Runtime;

public sealed record HealthCheckResult(string Name, bool Healthy, string Detail, TimeSpan Duration);

/// <summary>
/// SEKCJA 1 · pozycja 11 — Health Check Daemon (bez wątku w tle: sprawdzenia uruchamia host albo
/// polecenie „zdrowie”, więc nic nie zjada CPU, gdy nikt nie pyta).
/// <para>Sprawdzenie, które rzuci wyjątek, jest raportowane jako problem razem z treścią wyjątku —
/// nigdy jako „OK”. Czas każdego sprawdzenia jest jawnie podawany, żeby wolny czujnik nie udawał,
/// że wszystko działa szybko.</para>
/// </summary>
public sealed class HealthRegistry
{
    private readonly object gate = new();
    private readonly List<(string Name, Func<(bool Healthy, string Detail)> Probe)> checks = new();

    public int Count
    {
        get { lock (gate) return checks.Count; }
    }

    public void Register(string name, Func<(bool Healthy, string Detail)> probe)
    {
        string clean = (name ?? "").Trim();
        if (clean.Length == 0 || probe is null) return;
        lock (gate)
        {
            checks.RemoveAll(x => string.Equals(x.Name, clean, StringComparison.OrdinalIgnoreCase));
            checks.Add((clean, probe));
        }
    }

    /// <summary>Wygodna rejestracja: brak wyjątku = zdrowy, zwrócony tekst jest szczegółem.</summary>
    public void RegisterSimple(string name, Func<string> probe)
    {
        if (probe is null) return;
        Register(name, () => (true, probe()));
    }

    public IReadOnlyList<HealthCheckResult> RunAll()
    {
        (string Name, Func<(bool Healthy, string Detail)> Probe)[] snapshot;
        lock (gate) snapshot = checks.ToArray();
        var results = new List<HealthCheckResult>();
        foreach (var check in snapshot)
        {
            var watch = Stopwatch.StartNew();
            bool healthy;
            string detail;
            try
            {
                (healthy, detail) = check.Probe();
                if (detail is null) detail = healthy ? "bez szczegółów" : "brak szczegółów problemu";
            }
            catch (Exception ex)
            {
                healthy = false;
                detail = "wyjątek " + ex.GetType().Name + ": " + ex.Message;
            }
            watch.Stop();
            results.Add(new HealthCheckResult(check.Name, healthy, detail, watch.Elapsed));
        }
        return results;
    }

    public string Describe()
    {
        var results = RunAll();
        if (results.Count == 0) return "Nie mam zarejestrowanych sprawdzeń zdrowia — nic nie udaję zdrowego.";
        int problems = results.Count(x => !x.Healthy);
        var lines = new List<string>
        {
            "Zdrowie (" + (results.Count - problems) + "/" + results.Count + " OK" + (problems > 0 ? ", PROBLEMY: " + problems : "") + "):"
        };
        foreach (var result in results)
            lines.Add("· " + (result.Healthy ? "OK" : "PROBLEM") + " — " + result.Name + ": " + result.Detail +
                " (" + result.Duration.TotalMilliseconds.ToString("0.#") + " ms)");
        return string.Join(Environment.NewLine, lines);
    }
}
