using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SentinelX.Core.Runtime;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// SEKCJA 1 · pozycja 10 — Structured Logger (JSON logs z rotacją).
/// <para>Każda linia to jeden obiekt JSON: czas, poziom, zdarzenie, treść i pola dodatkowe.
/// Rotacja jest rozmiarowa: po przekroczeniu limitu plik wędruje do <c>.1</c>, starsze numery się
/// przesuwają, najstarszy jest usuwany — więc dziennik nie rośnie bez końca. Zapis jest dopisywany
/// i natychmiastowy (bez buforowania w pamięci), bo dziennik, który ginie przy awarii, nic nie daje.</para>
/// </summary>
public sealed class StructuredLog
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly object gate = new();
    private readonly long maxBytes;
    private readonly int keepFiles;
    private readonly LogLevel minimum;

    public StructuredLog(string? directory = null, LogLevel minimum = LogLevel.Info, int maxBytes = 512 * 1024, int keepFiles = 5)
    {
        Directory = directory ?? AppPaths.LogsDirectory;
        this.minimum = minimum;
        this.maxBytes = Math.Max(4096, maxBytes);
        this.keepFiles = Math.Clamp(keepFiles, 1, 20);
        Path = System.IO.Path.Combine(Directory, "structured.jsonl");
    }

    public string Directory { get; }
    public string Path { get; }
    public int Rotations { get; private set; }
    public long Written { get; private set; }
    public string? LastWriteError { get; private set; }

    public void Write(LogLevel level, string eventName, string message, IReadOnlyDictionary<string, string>? fields = null)
    {
        if (level < minimum) return;
        var record = new Dictionary<string, object?>
        {
            ["at"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            ["level"] = level.ToString().ToLowerInvariant(),
            ["event"] = Truncate(eventName, 80),
            ["message"] = Truncate(message, 500),
            ["process"] = Environment.ProcessId
        };
        if (fields is { Count: > 0 })
        {
            var clean = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in fields)
            {
                if (pair.Key.Length == 0) continue;
                clean[Truncate(pair.Key, 40)] = Truncate(pair.Value ?? "", 200);
            }
            record["fields"] = clean;
        }
        string line;
        try { line = JsonSerializer.Serialize(record, Json); }
        catch (Exception ex) { LastWriteError = "Nie zserializowałem wpisu: " + ex.Message; return; }

        lock (gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                RotateIfNeeded();
                File.AppendAllText(Path, line + Environment.NewLine, Encoding.UTF8);
                Written++;
                LastWriteError = null;
            }
            catch (Exception ex)
            {
                LastWriteError = "Nie zapisałem dziennika: " + ex.Message;
            }
        }
    }

    public void Debug(string eventName, string message, IReadOnlyDictionary<string, string>? fields = null) => Write(LogLevel.Debug, eventName, message, fields);
    public void Info(string eventName, string message, IReadOnlyDictionary<string, string>? fields = null) => Write(LogLevel.Info, eventName, message, fields);
    public void Warn(string eventName, string message, IReadOnlyDictionary<string, string>? fields = null) => Write(LogLevel.Warn, eventName, message, fields);
    public void Error(string eventName, string message, IReadOnlyDictionary<string, string>? fields = null) => Write(LogLevel.Error, eventName, message, fields);

    /// <summary>Ostatnie wpisy z aktywnego pliku (surowy JSON), najstarsze najpierw.</summary>
    public IReadOnlyList<string> Read(int max = 20)
    {
        try
        {
            if (!File.Exists(Path)) return [];
            var lines = File.ReadAllLines(Path);
            return lines.TakeLast(Math.Clamp(max, 1, 500)).ToArray();
        }
        catch (Exception ex)
        {
            LastWriteError = "Nie odczytałem dziennika: " + ex.Message;
            return [];
        }
    }

    /// <summary>Czytelny podgląd ostatnich wpisów: poziom, zdarzenie i treść z JSON-a.</summary>
    public string Describe(int max = 8)
    {
        var lines = Read(max);
        var header = "Dziennik JSONL: " + Path + " · " + Written + " wpisów w tej sesji · rotacji: " + Rotations +
            (LastWriteError is null ? "" : " · " + LastWriteError);
        if (lines.Count == 0) return header + Environment.NewLine + "Plik jest jeszcze pusty albo nie istnieje.";
        var rendered = new List<string>();
        foreach (string line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                string at = root.TryGetProperty("at", out var atElement) ? SafeTime(atElement.GetString()) : "";
                string level = root.TryGetProperty("level", out var levelElement) ? levelElement.GetString() ?? "" : "";
                string name = root.TryGetProperty("event", out var eventElement) ? eventElement.GetString() ?? "" : "";
                string message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "";
                rendered.Add("· " + at + " [" + level + "] " + name + ": " + message);
            }
            catch
            {
                rendered.Add("· (nieczytelna linia — zostawiam ją w pliku: " + Truncate(line, 80) + ")");
            }
        }
        return header + Environment.NewLine + string.Join(Environment.NewLine, rendered);
    }

    private static string SafeTime(string? iso)
    {
        if (iso is null) return "";
        return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
            ? moment.ToString("HH:mm:ss")
            : "";
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(Path)) return;
        if (new FileInfo(Path).Length < maxBytes) return;
        for (int index = keepFiles - 1; index >= 1; index--)
        {
            string older = Path + "." + index;
            if (!File.Exists(older)) continue;
            if (index + 1 >= keepFiles)
            {
                File.Delete(older);
                continue;
            }
            File.Move(older, Path + "." + (index + 1), true);
        }
        File.Move(Path, Path + ".1", true);
        Rotations++;
    }

    private static string Truncate(string? text, int max)
    {
        string value = text ?? "";
        return value.Length <= max ? value : value[..max] + "…";
    }
}
