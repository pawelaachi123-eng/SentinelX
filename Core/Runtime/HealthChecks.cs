using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SentinelX.Core.Runtime;

public sealed record HealthCheckResult(string Name, bool Healthy, string Detail, TimeSpan Duration);

/// <summary>
/// SEKCJA 1 ┬Ě pozycja 11 ÔÇö Health Check Daemon (bez w─ůtku w tle: sprawdzenia uruchamia host albo
/// polecenie ÔÇ×zdrowieÔÇŁ, wi─Öc nic nie zjada CPU, gdy nikt nie pyta).
/// <para>Sprawdzenie, kt├│re rzuci wyj─ůtek, jest raportowane jako problem razem z tre┼Ťci─ů wyj─ůtku ÔÇö
/// nigdy jako ÔÇ×OKÔÇŁ. Czas ka┼╝dego sprawdzenia jest jawnie podawany, ┼╝eby wolny czujnik nie udawa┼é,
/// ┼╝e wszystko dzia┼éa szybko.</para>
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

    /// <summary>Wygodna rejestracja: brak wyj─ůtku = zdrowy, zwr├│cony tekst jest szczeg├│┼éem.</summary>
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
                if (detail is null) detail = healthy ? "bez szczeg├│┼é├│w" : "brak szczeg├│┼é├│w problemu";
            }
            catch (Exception ex)
            {
                healthy = false;
                detail = "wyj─ůtek " + ex.GetType().Name + ": " + ex.Message;
            }
            watch.Stop();
            results.Add(new HealthCheckResult(check.Name, healthy, detail, watch.Elapsed));
        }
        return results;
    }

    public string Describe()
    {
        var results = RunAll();
        if (results.Count == 0) return "Nie mam zarejestrowanych sprawdze┼ä zdrowia ÔÇö nic nie udaj─Ö zdrowego.";
        int problems = results.Count(x => !x.Healthy);
        var lines = new List<string>
        {
            "Zdrowie (" + (results.Count - problems) + "/" + results.Count + " OK" + (problems > 0 ? ", PROBLEMY: " + problems : "") + "):"
        };
        foreach (var result in results)
            lines.Add("┬Ě " + (result.Healthy ? "OK" : "PROBLEM") + " ÔÇö " + result.Name + ": " + result.Detail +
                " (" + result.Duration.TotalMilliseconds.ToString("0.#") + " ms)");
        return string.Join(Environment.NewLine, lines);
    }
}
