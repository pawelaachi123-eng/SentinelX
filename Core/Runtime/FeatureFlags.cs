using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SentinelX.Core.Runtime;

public sealed record FeatureFlag(string Name, bool Enabled, int Percentage, string Description, DateTimeOffset UpdatedAt);

/// <summary>
/// SEKCJA 1 ┬Ě pozycja 41 ÔÇö Feature Flag System.
/// <para>Flaga ma stan (w┼é─ůczona/wy┼é─ůczona), procentowy zasi─Ög i opis. Zasi─Ög jest deterministyczny:
/// ten sam klucz (np. identyfikator sesji albo projekt) zawsze trafia do tej samej grupy, bo decyduje
/// skr├│t FNV-1a, nie losowanie. Dzi─Öki temu w┼é─ůczenie flagi ÔÇ×na 30%ÔÇŁ nie skacze mi─Ödzy uruchomieniami.</para>
/// <para>Zapis jest atomowy (plik tymczasowy + podmiana), a b┼é─ůd zapisu jest raportowany, nigdy cichy.
/// Brak pliku = brak flag, nie ÔÇ×wszystko w┼é─ůczoneÔÇŁ.</para>
/// </summary>
public sealed class FeatureFlags
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object gate = new();
    private readonly Dictionary<string, FeatureFlag> flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly string filePath;

    public FeatureFlags(string? path = null)
    {
        filePath = path ?? Path.Combine(AppPaths.MemoryDirectory, "FeatureFlags.json");
        Load();
    }

    public string FilePath => filePath;
    public string? LastStorageError { get; private set; }

    public IReadOnlyList<FeatureFlag> All()
    {
        lock (gate) return flags.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public FeatureFlag? Get(string name)
    {
        lock (gate) return flags.TryGetValue((name ?? "").Trim(), out var flag) ? flag : null;
    }

    public int Count
    {
        get { lock (gate) return flags.Count; }
    }

    /// <summary>Czy flaga jest aktywna dla danego podmiotu. Brak flagi = wy┼é─ůczone (bezpieczny domy┼Ťl).
    /// Flaga w┼é─ůczona w 100% dzia┼éa tak┼╝e bez podmiotu.</summary>
    public bool IsEnabled(string name, string? subject = null)
    {
        var flag = Get(name);
        if (flag is null || !flag.Enabled) return false;
        if (flag.Percentage >= 100) return true;
        if (flag.Percentage <= 0) return false;
        string key = subject ?? "domyslny";
        return Bucket(flag.Name + "|" + key) < flag.Percentage;
    }

    public bool Set(string name, bool enabled, int percentage = 100, string description = "")
    {
        string clean = (name ?? "").Trim();
        if (clean.Length is 0 or > 120) return false;
        if (clean.Any(char.IsWhiteSpace)) return false;
        int clamped = Math.Clamp(percentage, 0, 100);
        lock (gate)
        {
            flags[clean] = new FeatureFlag(clean, enabled, clamped, description.Length > 200 ? description[..200] : description, DateTimeOffset.Now);
            SaveLocked();
        }
        return true;
    }

    public bool Remove(string name)
    {
        lock (gate)
        {
            bool removed = flags.Remove((name ?? "").Trim());
            if (removed) SaveLocked();
            return removed;
        }
    }

    /// <summary>Deterministyczny przydzia┼é do grupy 0ÔÇô99 (skr├│t FNV-1a, ten sam wynik na ka┼╝dej maszynie).</summary>
    internal static int Bucket(string key)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        uint hash = offset;
        foreach (char character in key)
        {
            hash ^= character;
            hash *= prime;
        }
        return (int)(hash % 100);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(filePath)) return;
            string json = File.ReadAllText(filePath);
            var parsed = JsonSerializer.Deserialize<List<FeatureFlag>>(json, Json);
            if (parsed is null) return;
            foreach (var flag in parsed)
            {
                if (flag.Name is null || flag.Name.Length == 0) continue;
                flags[flag.Name] = flag with { Percentage = Math.Clamp(flag.Percentage, 0, 100) };
            }
        }
        catch (Exception ex)
        {
            LastStorageError = "Nie odczyta┼éem flag (zostawiam czysty stan, plik nietkni─Öty): " + ex.Message;
        }
    }

    private void SaveLocked()
    {
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string payload = JsonSerializer.Serialize(flags.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(), Json);
            string temporary = filePath + ".tmp";
            File.WriteAllText(temporary, payload);
            File.Move(temporary, filePath, true);
            string readBack = File.ReadAllText(filePath);
            LastStorageError = readBack.Length == payload.Length
                ? null
                : "Zapis flag nie zgadza si─Ö z odczytem zwrotnym ÔÇö sprawd┼║ plik " + filePath;
        }
        catch (Exception ex)
        {
            LastStorageError = "Nie zapisa┼éem flag: " + ex.Message;
        }
    }

    public string Describe()
    {
        var all = All();
        var lines = new List<string>
        {
            "Flagi funkcji: " + all.Count + (LastStorageError is null ? "" : " ┬Ě " + LastStorageError)
        };
        if (all.Count == 0) lines.Add("Brak zdefiniowanych flag ÔÇö nic nie jest w┼é─ůczane po cichu. Ustaw: ÔÇ×ustaw flage: nazwa on|off|30%ÔÇŁ.");
        else lines.AddRange(all.Select(x => "┬Ě " + x.Name + ": " + (x.Enabled ? "w┼é─ůczona" : "wy┼é─ůczona") +
            " ┬Ě zasi─Ög " + x.Percentage + "%" + (x.Description.Length > 0 ? " ┬Ě " + x.Description : "") +
            " ┬Ě zmiana " + x.UpdatedAt.ToString("yyyy-MM-dd HH:mm")));
        return string.Join(Environment.NewLine, lines);
    }
}
