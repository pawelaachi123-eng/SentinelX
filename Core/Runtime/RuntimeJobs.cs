using System;
using System.Collections.Generic;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>
/// Wykonawca zada┼ä rdzenia ÔÇö to prawdziwa praca, nie atrapa. Obs┼éugiwane rodzaje zada┼ä:
/// <list type="bullet">
/// <item><c>log: poziom tre┼Ť─ç</c> ÔÇö wpis do dziennika JSONL (poziomy: debug, info, warn, error),</item>
/// <item><c>cache: klucz = warto┼Ť─ç</c> ÔÇö zapis do cache (TTL 10 minut),</item>
/// <item><c>cache-usun: klucz</c> ÔÇö usuni─Öcie wpisu,</item>
/// <item><c>metryka: nazwa warto┼Ť─ç</c> ÔÇö dopisanie pr├│bki do histogramu,</item>
/// <item><c>flaga: nazwa on|off|procent</c> ÔÇö zmiana flagi funkcji.</item>
/// </list>
/// Ka┼╝dy inny rodzaj ko┼äczy si─Ö b┼é─Ödem zadania (trafia do ponowie┼ä i kolejki zwrot├│w) ÔÇö wykonawca
/// nigdy nie m├│wi ÔÇ×zrobioneÔÇŁ, gdy nie wie, co mia┼é zrobi─ç.
/// </summary>
public static class RuntimeJobs
{
    public static string? Execute(SentinelRuntime runtime, QueuedTask task)
    {
        string name = task.Name.Trim();
        int colon = name.IndexOf(':');
        if (colon <= 0) return "Nieznany rodzaj zadania ÔÇ×" + name + "ÔÇŁ. U┼╝yj: log:, cache:, cache-usun:, metryka:, flaga:.";
        string kind = name[..colon].Trim().ToLowerInvariant();
        string argument = name[(colon + 1)..].Trim();
        if (argument.Length == 0) return "Zadanie ÔÇ×" + kind + "ÔÇŁ nie ma tre┼Ťci po dwukropku.";

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
                if (equals <= 0) return "Zapis do cache wymaga postaci ÔÇ×klucz = warto┼Ť─çÔÇŁ.";
                string key = argument[..equals].Trim();
                string value = argument[(equals + 1)..].Trim();
                if (key.Length == 0) return "Klucz cache nie mo┼╝e by─ç pusty.";
                runtime.Cache.Set(key, value, TimeSpan.FromMinutes(10));
                return null;
            }
            case "cache-usun":
            {
                bool removed = runtime.Cache.Remove(argument);
                return removed ? null : "W cache nie by┼éo klucza ÔÇ×" + argument + "ÔÇŁ ÔÇö nie zg┼éaszam sukcesu dla niczego.";
            }
            case "metryka":
            {
                string[] parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return "Metryka wymaga nazwy i warto┼Ťci, np. ÔÇ×metryka: cpu 42ÔÇŁ.";
                if (!double.TryParse(parts[^1].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                    return "Warto┼Ť─ç ÔÇ×" + parts[^1] + "ÔÇŁ nie jest liczb─ů.";
                runtime.Metrics.Observe(string.Join(" ", parts[..^1]), value);
                return null;
            }
            case "flaga":
            {
                string[] parts = argument.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return "Flaga wymaga nazwy i stanu, np. ÔÇ×flaga: eksperyment onÔÇŁ.";
                string flagName = parts[0];
                string state = parts[1].ToLowerInvariant().TrimEnd('%');
                bool enabled = state is not ("off" or "wylacz" or "wylaczona" or "0");
                int percentage = enabled ? 100 : 0;
                if (state is "0" or "off") percentage = 0;
                else if (int.TryParse(state, out int parsed)) { enabled = parsed > 0; percentage = parsed; }
                return runtime.Flags.Set(flagName, enabled, percentage, "ustawione zadaniem kolejki")
                    ? null
                    : "Nie ustawi┼éem flagi ÔÇ×" + flagName + "ÔÇŁ ÔÇö nazwa jest niepoprawna.";
            }
            default:
                return "Nieznany rodzaj zadania ÔÇ×" + kind + "ÔÇŁ. U┼╝yj: log:, cache:, cache-usun:, metryka:, flaga:.";
        }
    }

    public static string Describe()
    {
        return """
            Wykonawca rdzenia obs┼éuguje zadania tekstowe:
            ┬Ě log: info tre┼Ť─ç        ÔÇö wpis do dziennika JSONL (debug/info/warn/error)
            ┬Ě cache: klucz = warto┼Ť─ç ÔÇö zapis do cache na 10 minut
            ┬Ě cache-usun: klucz      ÔÇö usuni─Öcie wpisu z cache
            ┬Ě metryka: cpu 42        ÔÇö pr├│bka do histogramu (nazwa mo┼╝e mie─ç spacje)
            ┬Ě flaga: eksperyment on  ÔÇö flaga funkcji (on/off/0-100)
            """;
    }
}
