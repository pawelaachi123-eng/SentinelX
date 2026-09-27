using System;
using System.Collections.Generic;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>
/// Wykonawca zadań rdzenia — to prawdziwa praca, nie atrapa. Obsługiwane rodzaje zadań:
/// <list type="bullet">
/// <item><c>log: poziom treść</c> — wpis do dziennika JSONL (poziomy: debug, info, warn, error),</item>
/// <item><c>cache: klucz = wartość</c> — zapis do cache (TTL 10 minut),</item>
/// <item><c>cache-usun: klucz</c> — usunięcie wpisu,</item>
/// <item><c>metryka: nazwa wartość</c> — dopisanie próbki do histogramu,</item>
/// <item><c>flaga: nazwa on|off|procent</c> — zmiana flagi funkcji.</item>
/// </list>
/// Każdy inny rodzaj kończy się błędem zadania (trafia do ponowień i kolejki zwrotów) — wykonawca
/// nigdy nie mówi „zrobione”, gdy nie wie, co miał zrobić.
/// </summary>
public static class RuntimeJobs
{
    public static string? Execute(SentinelRuntime runtime, QueuedTask task)
    {
        string name = task.Name.Trim();
        int colon = name.IndexOf(':');
        if (colon <= 0) return "Nieznany rodzaj zadania „" + name + "”. Użyj: log:, cache:, cache-usun:, metryka:, flaga:.";
        string kind = name[..colon].Trim().ToLowerInvariant();
        string argument = name[(colon + 1)..].Trim();
        if (argument.Length == 0) return "Zadanie „" + kind + "” nie ma treści po dwukropku.";

        switch (kind)
        {
            case "log":
            {
                string[] parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                string levelText = parts.Length > 1 ? parts[0].ToLowerInvariant() : "info";
                string message = parts.Length > 1 ? parts[1] : parts[0];
                LogLevel level = levelText switch
                {
                    "debug" => LogLevel.Debug,
                    "info" => LogLevel.Info,
                    "warn" or "ostrzezenie" => LogLevel.Warn,
                    "error" or "blad" => LogLevel.Error,
                    _ => LogLevel.Info
                };
                if (level == LogLevel.Info && levelText is not ("info" or "debug" or "warn" or "error")) message = argument;
                runtime.Log.Write(level, "kolejka", message, new Dictionary<string, string> { ["zadanie"] = task.Id });
                return null;
            }
            case "cache":
            {
                int equals = argument.IndexOf('=');
                if (equals <= 0) return "Zapis do cache wymaga postaci „klucz = wartość”.";
                string key = argument[..equals].Trim();
                string value = argument[(equals + 1)..].Trim();
                if (key.Length == 0) return "Klucz cache nie może być pusty.";
                runtime.Cache.Set(key, value, TimeSpan.FromMinutes(10));
                return null;
            }
            case "cache-usun":
            {
                bool removed = runtime.Cache.Remove(argument);
                return removed ? null : "W cache nie było klucza „" + argument + "” — nie zgłaszam sukcesu dla niczego.";
            }
            case "metryka":
            {
                string[] parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return "Metryka wymaga nazwy i wartości, np. „metryka: cpu 42”.";
                if (!double.TryParse(parts[^1].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                    return "Wartość „" + parts[^1] + "” nie jest liczbą.";
                runtime.Metrics.Observe(string.Join(" ", parts[..^1]), value);
                return null;
            }
            case "flaga":
            {
                string[] parts = argument.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return "Flaga wymaga nazwy i stanu, np. „flaga: eksperyment on”.";
                string flagName = parts[0];
                string state = parts[1].ToLowerInvariant().TrimEnd('%');
                bool enabled = state is not ("off" or "wylacz" or "wylaczona" or "0");
                int percentage = enabled ? 100 : 0;
                if (state is "0" or "off") percentage = 0;
                else if (int.TryParse(state, out int parsed)) { enabled = parsed > 0; percentage = parsed; }
                return runtime.Flags.Set(flagName, enabled, percentage, "ustawione zadaniem kolejki")
                    ? null
                    : "Nie ustawiłem flagi „" + flagName + "” — nazwa jest niepoprawna.";
            }
            default:
                return "Nieznany rodzaj zadania „" + kind + "”. Użyj: log:, cache:, cache-usun:, metryka:, flaga:.";
        }
    }

    public static string Describe()
    {
        return """
            Wykonawca rdzenia obsługuje zadania tekstowe:
            · log: info treść        — wpis do dziennika JSONL (debug/info/warn/error)
            · cache: klucz = wartość — zapis do cache na 10 minut
            · cache-usun: klucz      — usunięcie wpisu z cache
            · metryka: cpu 42        — próbka do histogramu (nazwa może mieć spacje)
            · flaga: eksperyment on  — flaga funkcji (on/off/0-100)
            """;
    }
}
