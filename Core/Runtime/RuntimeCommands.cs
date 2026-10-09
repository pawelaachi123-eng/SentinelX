using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>
/// SEKCJA 1 ÔÇö warstwa polece┼ä rdzenia. Wszystko dzieje si─Ö lokalnie, a ka┼╝da odpowied┼║ m├│wi, co
/// naprawd─Ö zosta┼éo zrobione (ÔÇ×zapisano i sprawdzonoÔÇŁ, ÔÇ×nie uda┼éo si─Ö, boÔÇŽÔÇŁ). Polecenia maj─ů w┼éasne,
/// jednoznaczne frazy, ┼╝eby nie kolidowa┼éy z istniej─ůcymi narz─Ödziami Sentinel X.
/// </summary>
public static class RuntimeCommands
{
    private static SentinelRuntime runtime = SentinelRuntime.Instance;

    public static SentinelRuntime Runtime
    {
        get => runtime;
        set => runtime = value;
    }

    private static SentinelRuntime Prepare()
    {
        var current = runtime;
        if (current.Queue.Worker is null)
            current.Queue.RegisterWorker(task => RuntimeJobs.Execute(current, task), "rdze┼ä");
        return current;
    }

    /// <summary>Zwraca odpowied┼║, gdy polecenie nale┼╝y do rdzenia; null = nie moje.</summary>
    public static string? TryHandle(string command, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var core = Prepare();

        if (text is "rdzen" or "stan rdzenia" or "core" or "sentinel rdzen")
        {
            core.Log.Info("rdzen.raport", "raport rdzenia na ┼╝─ůdanie");
            return core.Describe();
        }
        if (text is "zdarzenia" or "dziennik zdarzen" or "eventy" or "ostatnie zdarzenia")
            return core.Events.Describe();
        if (text is "metryki" or "pomiary" or "profil wydajnosci" or "metryki rdzenia")
            return core.Metrics.Describe();
        if (text is "zdrowie" or "zdrowie systemu" or "health" or "stan zdrowia")
            return core.Health.Describe();
        if (text is "dziennik json" or "logi json" or "dziennik jsonl" or "log strukturalny")
            return core.Log.Describe();
        if (text is "bezpieczniki" or "obwody" or "stany obwodow")
            return core.Breakers.Count == 0
                ? "Nie ma jeszcze ┼╝adnego bezpiecznika ÔÇö powstaje przy pierwszym u┼╝yciu, np. ÔÇ×kolejkaÔÇŁ."
                : "Bezpieczniki:" + Environment.NewLine + string.Join(Environment.NewLine, core.Breakers.Values.Select(x => x.Describe()));
        if (text is "maszyna" or "cykl zycia" or "stan maszyny" or "maszyna stanow")
            return "Maszyna stan├│w cyklu ┼╝ycia:" + Environment.NewLine + core.Lifecycle.Describe();
        if (text is "cache" or "pamiec podreczna" or "stan cache")
            return core.Cache.Describe() + Environment.NewLine + RuntimeJobs.Describe();

        // --- kolejka zada┼ä ---
        if (text is "kolejka" or "kolejka zadan" or "stan kolejki" or "zadania w tle")
            return core.Queue.Describe() + Environment.NewLine + RuntimeJobs.Describe();
        if (text.StartsWith("kolejka dodaj:", StringComparison.Ordinal))
        {
            string payload = command.Trim()[("kolejka dodaj:".Length)..].Trim();
            if (payload.Length == 0) return "Podaj tre┼Ť─ç zadania, np. ÔÇ×kolejka dodaj: log: info testÔÇŁ.";
            var priority = RuntimePriority.Normal;
            foreach (string prefix in new[] { "krytyczne ", "krytyczny ", "pilne " })
                if (payload.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { priority = RuntimePriority.Critical; payload = payload[prefix.Length..]; }
            if (priority == RuntimePriority.Normal)
                foreach (string prefix in new[] { "wysokie ", "wysoki " })
                    if (payload.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { priority = RuntimePriority.High; payload = payload[prefix.Length..]; }
            if (priority == RuntimePriority.Normal)
                foreach (string prefix in new[] { "niskie ", "niski " })
                    if (payload.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { priority = RuntimePriority.Low; payload = payload[prefix.Length..]; }
            var task = core.Queue.Enqueue(payload, priority);
            return "Dodano " + task.Id + " [" + priority + "]: " + task.Name + Environment.NewLine +
                "Wykonanie: ÔÇ×kolejka przetworzÔÇŁ (bez wykonawcy kolejka odmawia pracy, zamiast udawa─ç).";
        }
        if (text is "kolejka przetworz" or "przetworz kolejke")
            return core.Queue.RunDue(5);
        if (text.StartsWith("kolejka przetworz ", StringComparison.Ordinal) &&
            int.TryParse(text["kolejka przetworz ".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int howMany))
            return core.Queue.RunDue(Math.Clamp(howMany, 1, 50));
        if (text.StartsWith("kolejka anuluj:", StringComparison.Ordinal))
        {
            string id = text["kolejka anuluj:".Length..].Trim();
            return core.Queue.Cancel(id)
                ? "Zadanie " + id.ToUpperInvariant() + " anulowane (nie zostanie wykonane)."
                : "Nie anulowa┼éem: nie ma zadania ÔÇ×" + id + "ÔÇŁ w stanie oczekiwania.";
        }
        if (text is "zwroty" or "kolejka zwrotow" or "dead letter" or "porzucone zadania")
            return core.DeadLetters.Describe();
        if (text.StartsWith("zwrot ponow:", StringComparison.Ordinal))
        {
            string id = text["zwrot ponow:".Length..].Trim();
            if (!core.DeadLetters.TryTake(id, out var letter) || letter is null)
                return "Nie ma w kolejce zwrot├│w zadania ÔÇ×" + id + "ÔÇŁ.";
            var requeued = core.Queue.Enqueue(letter.Task, letter.Priority);
            return "Wskrzeszone: " + letter.Id + " Ôćĺ " + requeued.Id + " [" + requeued.Priority + "] " + requeued.Name +
                Environment.NewLine + "Wykonanie: ÔÇ×kolejka przetworzÔÇŁ.";
        }

        // --- cron ---
        if (text.StartsWith("cron ", StringComparison.Ordinal))
        {
            int colon = text.IndexOf(':');
            if (colon < 0) return "U┼╝yj: ÔÇ×cron opis: */15 * * * *ÔÇŁ albo ÔÇ×cron nastepne: 0 8 * * 1-5ÔÇŁ.";
            string expression = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            if (!CronSchedule.TryParse(expression, out var schedule, out string error) || schedule is null)
                return "Nie rozumiem tego crona: " + error;
            return "Cron ÔÇ×" + schedule.Expression + "ÔÇŁ Ôćĺ " + schedule.Describe() + Environment.NewLine + schedule.DescribeNext(5);
        }

        // --- flagi funkcji ---
        if (text is "flagi" or "flagi funkcji" or "lista flag")
            return core.Flags.Describe();
        if (text.StartsWith("flaga ", StringComparison.Ordinal))
        {
            string[] parts = command.Trim()["flaga ".Length..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "Podaj nazw─Ö flagi: ÔÇ×flaga eksperymentÔÇŁ albo ÔÇ×flaga eksperyment sesja-42ÔÇŁ.";
            string? subject = parts.Length > 1 ? parts[1] : null;
            bool enabled = core.Flags.IsEnabled(parts[0], subject);
            var flag = core.Flags.Get(parts[0]);
            return flag is null
                ? "Nie ma flagi ÔÇ×" + parts[0] + "ÔÇŁ. Nie wymy┼Ťlam warto┼Ťci ÔÇö brak flagi znaczy ÔÇ×wy┼é─ůczoneÔÇŁ."
                : "Flaga ÔÇ×" + flag.Name + "ÔÇŁ: " + (flag.Enabled ? "w┼é─ůczona" : "wy┼é─ůczona") + " ┬Ě zasi─Ög " + flag.Percentage + "%" +
                  (subject is null ? " ┬Ě bez podmiotu (grupa liczona z klucza ÔÇ×domyslnyÔÇŁ)" : " ┬Ě podmiot ÔÇ×" + subject + "ÔÇŁ") +
                  " Ôćĺ " + (enabled ? "AKTYWNA" : "nieaktywna") + ".";
        }
        if (text.StartsWith("ustaw flage:", StringComparison.Ordinal) || text.StartsWith("ustaw flage ", StringComparison.Ordinal))
        {
            string payload = text.StartsWith("ustaw flage:", StringComparison.Ordinal)
                ? command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim()
                : command.Trim()["ustaw flage ".Length..].Trim();
            string[] parts = payload.Split([' ', '\t', '='], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "U┼╝yj: ÔÇ×ustaw flage: eksperyment onÔÇŁ, ÔÇ×ÔÇŽ offÔÇŁ albo ÔÇ×ÔÇŽ 30ÔÇŁ (procent).";
            string name = parts[0];
            string state = parts[1].ToLowerInvariant().TrimEnd('%');
            bool enabled;
            int percentage;
            if (state is "on" or "wlacz" or "wlaczona" or "true" or "100") { enabled = true; percentage = 100; }
            else if (state is "off" or "wylacz" or "wylaczona" or "false" or "0") { enabled = false; percentage = 0; }
            else if (int.TryParse(state, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed is >= 0 and <= 100) { enabled = parsed > 0; percentage = parsed; }
            else return "Stan flagi to ÔÇ×onÔÇŁ, ÔÇ×offÔÇŁ albo liczba 0ÔÇô100 (procent u┼╝ytkownik├│w).";
            return core.Flags.Set(name, enabled, percentage, "ustawione poleceniem")
                ? "Flaga ÔÇ×" + name + "ÔÇŁ: " + (enabled ? "w┼é─ůczona" : "wy┼é─ůczona") + " ┬Ě zasi─Ög " + percentage + "%." +
                  (core.Flags.LastStorageError is null ? " Zapisano i sprawdzono odczytem zwrotnym." : " " + core.Flags.LastStorageError)
                : "Nie ustawi┼éem flagi ÔÇö nazwa musi by─ç jednym s┼éowem bez spacji (max 120 znak├│w).";
        }

        // --- cache ---
        if (text.StartsWith("cache zapisz:", StringComparison.Ordinal))
        {
            string payload = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            int equals = payload.IndexOf('=');
            if (equals <= 0) return "U┼╝yj: ÔÇ×cache zapisz: klucz = warto┼Ť─çÔÇŁ.";
            string key = payload[..equals].Trim();
            string value = payload[(equals + 1)..].Trim();
            core.Cache.Set(key, value, TimeSpan.FromMinutes(10));
            return "W cache: ÔÇ×" + key + "ÔÇŁ (TTL 10 minut). " + core.Cache.Describe();
        }
        if (text.StartsWith("cache pokaz:", StringComparison.Ordinal))
        {
            string key = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            return core.Cache.TryGet(key, out string? value)
                ? "ÔÇ×" + key + "ÔÇŁ = " + value
                : "Nie ma w cache klucza ÔÇ×" + key + "ÔÇŁ (albo wygas┼é). " + core.Cache.Describe();
        }
        if (text.StartsWith("cache usun:", StringComparison.Ordinal))
        {
            string key = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            return core.Cache.Remove(key) ? "Usuni─Öto z cache: ÔÇ×" + key + "ÔÇŁ." : "Nie by┼éo w cache klucza ÔÇ×" + key + "ÔÇŁ.";
        }

        // --- integralno┼Ť─ç plik├│w ---
        if (text is "integralnosc" or "manifesty" or "integralnosc plikow")
            return DescribeManifests(core);
        if (text.StartsWith("integralnosc zbuduj:", StringComparison.Ordinal))
        {
            string target = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim().Trim('"');
            if (!Directory.Exists(target)) return "Nie ma katalogu: " + target;
            var entries = IntegrityManifest.Build(target);
            if (entries.Count == 0) return "Katalog jest pusty ÔÇö nie buduj─Ö pustego manifestu.";
            string path = ManifestPath(core, target);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, IntegrityManifest.Serialize(entries));
            return "Manifest zapisany i sprawdzony odczytem zwrotnym." + Environment.NewLine +
                "┬Ě plik: " + path + Environment.NewLine + "┬Ě " + IntegrityManifest.Describe(entries);
        }
        if (text.StartsWith("integralnosc sprawdz:", StringComparison.Ordinal))
        {
            string target = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim().Trim('"');
            if (!Directory.Exists(target)) return "Nie ma katalogu: " + target;
            string path = ManifestPath(core, target);
            if (!File.Exists(path)) return "Nie mam manifestu dla tego katalogu. Najpierw: ÔÇ×integralnosc zbuduj: " + target + "ÔÇŁ.";
            if (!IntegrityManifest.TryDeserialize(File.ReadAllText(path), out var expected, out string error)) return error;
            var actual = IntegrityManifest.Build(target);
            return "Manifest: " + path + Environment.NewLine + IntegrityManifest.Compare(expected, actual);
        }

        // --- kopie zapasowe ---
        if (text is "kopie danych" or "kopie zapasowe" or "lista kopii" or "backup danych")
            return core.Backups.Describe();
        if (text is "kopia danych" or "zrob kopie danych" or "wykonaj kopie danych")
        {
            string report = core.Backups.Create(core.DataDirectory, "dane", [".json", ".jsonl", ".md", ".txt"],
                [core.BackupDirectory, Path.Combine(core.DataDirectory, "Cache")]);
            core.Log.Info("kopia.danych", "wykonano kopi─Ö danych");
            return report;
        }
        if (text.StartsWith("weryfikuj kopie:", StringComparison.Ordinal) || text.StartsWith("sprawdz kopie:", StringComparison.Ordinal))
        {
            string name = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            var backup = core.Backups.List().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? core.Backups.List().FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            return backup is null ? "Nie ma kopii o nazwie ÔÇ×" + name + "ÔÇŁ. " + core.Backups.Describe() : core.Backups.Verify(backup);
        }

        // --- sejf na sekrety ---
        if (text is "sejf" or "sejf status" or "stan sejfu")
            return core.Vault.Describe();
        if (text is "sejf odblokuj" or "odblokuj sejf")
        {
            var passphrase = core.Vault.PassphraseProvider?.Invoke();
            if (passphrase is null)
                return "Nie przyjmuj─Ö has┼éa z tre┼Ťci polecenia ÔÇö polecenia trafiaj─ů do historii i audytu, wi─Öc has┼éo w czacie jest has┼éem ujawnionym. " +
                       "U┼╝yj pola has┼éa w panelu Sejf (host przekazuje je bezpiecznym kana┼éem).";
            return core.Vault.Unlock(passphrase)
                ? "Sejf odblokowany. Wpisy odszyfrowane tylko w pami─Öci procesu."
                : "Nie odblokowa┼éem sejfu: " + (core.Vault.LastError ?? "nieznany pow├│d");
        }
        if (text is "sejf zablokuj" or "zablokuj sejf")
        {
            core.Vault.Lock();
            return "Sejf zablokowany, klucz wyzerowany w pami─Öci.";
        }
        if (text.StartsWith("sejf utworz", StringComparison.Ordinal))
        {
            var passphrase = core.Vault.PassphraseProvider?.Invoke();
            if (passphrase is null) return "Utworzenie sejfu wymaga has┼éa z pola w panelu (nie z czatu) ÔÇö inaczej has┼éo zosta┼éoby w historii polece┼ä.";
            if (core.Vault.Exists) return "Sejf ju┼╝ istnieje. U┼╝yj ÔÇ×sejf odblokujÔÇŁ.";
            return core.Vault.Create(passphrase) ? "Sejf utworzony (AES-256-GCM, klucz z PBKDF2)." : "Nie utworzy┼éem sejfu: " + core.Vault.LastError;
        }
        if (text.StartsWith("sejf dodaj:", StringComparison.Ordinal))
        {
            string payload = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            int equals = payload.IndexOf('=');
            if (equals <= 0) return "U┼╝yj: ÔÇ×sejf dodaj: nazwa = warto┼Ť─çÔÇŁ. Uwaga: warto┼Ť─ç w tre┼Ťci polecenia trafia do historii rozmowy ÔÇö ┼Ťwiadomie.";
            string name = payload[..equals].Trim();
            string secret = payload[(equals + 1)..].Trim();
            return core.Vault.Add(name, secret, "dodane poleceniem")
                ? "Zapisano sekret ÔÇ×" + name + "ÔÇŁ. Warto┼Ťci nie pokazuj─Ö w potwierdzeniu."
                : "Nie zapisa┼éem sekretu: " + (core.Vault.LastError ?? "pow├│d nieznany");
        }
        if (text.StartsWith("sejf pokaz:", StringComparison.Ordinal))
        {
            string name = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            string? secret = core.Vault.Get(name);
            return secret is null ? "Nie pokazuj─Ö sekretu: " + (core.Vault.LastError ?? "brak wpisu") : "Sekret ÔÇ×" + name + "ÔÇŁ: " + secret;
        }
        if (text.StartsWith("sejf usun:", StringComparison.Ordinal))
        {
            string name = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            return core.Vault.Remove(name) ? "Usuni─Öto wpis ÔÇ×" + name + "ÔÇŁ z sejfu." : "Nie usun─ů┼éem: " + (core.Vault.LastError ?? "brak wpisu");
        }

        // --- maszyna stan├│w ---
        if (text.StartsWith("maszyna:", StringComparison.Ordinal) || text.StartsWith("maszyna ", StringComparison.Ordinal))
        {
            string target = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            if (target.Length == 0) return "Podaj stan docelowy, np. ÔÇ×maszyna: startujeÔÇŁ.";
            return core.Lifecycle.TryGo(target, out string reason, "polecenie u┼╝ytkownika")
                ? "Przej┼Ťcie wykonane. " + Environment.NewLine + core.Lifecycle.Describe()
                : reason + Environment.NewLine + core.Lifecycle.Describe();
        }

        // --- workflow (DAG) ---
        if (text.StartsWith("workflow:", StringComparison.Ordinal) || text.StartsWith("graf:", StringComparison.Ordinal) ||
            text.StartsWith("dag:", StringComparison.Ordinal))
        {
            string spec = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            var graph = WorkflowGraph.Parse(spec, out string parseError);
            if (parseError.Length > 0) return parseError;
            return "Analiza grafu zada┼ä (nic nie uruchamiam ÔÇö to plan):" + Environment.NewLine + graph.Describe();
        }

        // --- serializacja binarna ---
        if (text.StartsWith("serializuj:", StringComparison.Ordinal))
        {
            string payload = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            try
            {
                byte[] encoded = BinarySerializer.Encode(new Dictionary<string, object?> { ["tekst"] = payload, ["dlugosc"] = (long)payload.Length });
                return BinarySerializer.Describe(encoded);
            }
            catch (NotSupportedException ex)
            {
                return ex.Message;
            }
        }
        if (text.StartsWith("deserializuj:", StringComparison.Ordinal))
        {
            string payload = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            if (!BinarySerializer.TryFromHex(payload, out byte[] data, out string hexError)) return hexError;
            if (!BinarySerializer.TryDecode(data, out var values, out string decodeError)) return decodeError;
            return "Odczytane pola (" + values.Count + "):" + Environment.NewLine +
                string.Join(Environment.NewLine, values.Select(x => "┬Ě " + x.Key + " = " + Format(x.Value)));
        }

        return null;
    }

    private static string Format(object? value) => value switch
    {
        null => "(null)",
        byte[] bytes => bytes.Length + " bajt├│w",
        IReadOnlyList<string> list => "[" + string.Join(", ", list) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static string ManifestPath(SentinelRuntime core, string target)
    {
        string label = new string(target.Where(char.IsLetterOrDigit).ToArray());
        if (label.Length == 0) label = "katalog";
        if (label.Length > 40) label = label[^40..];
        return Path.Combine(core.DataDirectory, "Memory", "Integrity", label + ".manifest.json");
    }

    private static string DescribeManifests(SentinelRuntime core)
    {
        string directory = Path.Combine(core.DataDirectory, "Memory", "Integrity");
        if (!Directory.Exists(directory)) return "Nie ma jeszcze ┼╝adnego manifestu. Zbuduj: ÔÇ×integralnosc zbuduj: <katalog>ÔÇŁ.";
        var files = Directory.GetFiles(directory, "*.manifest.json");
        if (files.Length == 0) return "Katalog manifest├│w jest pusty.";
        var lines = new List<string> { "Zapisane manifesty (" + files.Length + "):" };
        lines.AddRange(files.Select(x => "┬Ě " + Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(x)) +
            " ┬Ě " + new FileInfo(x).LastWriteTime.ToString("yyyy-MM-dd HH:mm") + " ┬Ě " + new FileInfo(x).Length + " B"));
        lines.Add("Sprawdzenie: ÔÇ×integralnosc sprawdz: <katalog>ÔÇŁ.");
        return string.Join(Environment.NewLine, lines);
    }
}
