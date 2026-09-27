using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>
/// SEKCJA 1 — warstwa poleceń rdzenia. Wszystko dzieje się lokalnie, a każda odpowiedź mówi, co
/// naprawdę zostało zrobione („zapisano i sprawdzono”, „nie udało się, bo…”). Polecenia mają własne,
/// jednoznaczne frazy, żeby nie kolidowały z istniejącymi narzędziami Sentinel X.
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
            current.Queue.RegisterWorker(task => RuntimeJobs.Execute(current, task), "rdzeń");
        return current;
    }

    /// <summary>Zwraca odpowiedź, gdy polecenie należy do rdzenia; null = nie moje.</summary>
    public static string? TryHandle(string command, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var core = Prepare();

        if (text is "rdzen" or "stan rdzenia" or "core" or "sentinel rdzen")
        {
            core.Log.Info("rdzen.raport", "raport rdzenia na żądanie");
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
                ? "Nie ma jeszcze żadnego bezpiecznika — powstaje przy pierwszym użyciu, np. „kolejka”."
                : "Bezpieczniki:" + Environment.NewLine + string.Join(Environment.NewLine, core.Breakers.Values.Select(x => x.Describe()));
        if (text is "maszyna" or "cykl zycia" or "stan maszyny" or "maszyna stanow")
            return "Maszyna stanów cyklu życia:" + Environment.NewLine + core.Lifecycle.Describe();
        if (text is "cache" or "pamiec podreczna" or "stan cache")
            return core.Cache.Describe() + Environment.NewLine + RuntimeJobs.Describe();

        // --- kolejka zadań ---
        if (text is "kolejka" or "kolejka zadan" or "stan kolejki" or "zadania w tle")
            return core.Queue.Describe() + Environment.NewLine + RuntimeJobs.Describe();
        if (text.StartsWith("kolejka dodaj:", StringComparison.Ordinal))
        {
            string payload = command.Trim()[("kolejka dodaj:".Length)..].Trim();
            if (payload.Length == 0) return "Podaj treść zadania, np. „kolejka dodaj: log: info test”.";
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
                "Wykonanie: „kolejka przetworz” (bez wykonawcy kolejka odmawia pracy, zamiast udawać).";
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
                : "Nie anulowałem: nie ma zadania „" + id + "” w stanie oczekiwania.";
        }
        if (text is "zwroty" or "kolejka zwrotow" or "dead letter" or "porzucone zadania")
            return core.DeadLetters.Describe();
        if (text.StartsWith("zwrot ponow:", StringComparison.Ordinal))
        {
            string id = text["zwrot ponow:".Length..].Trim();
            if (!core.DeadLetters.TryTake(id, out var letter) || letter is null)
                return "Nie ma w kolejce zwrotów zadania „" + id + "”.";
            var requeued = core.Queue.Enqueue(letter.Task, letter.Priority);
            return "Wskrzeszone: " + letter.Id + " → " + requeued.Id + " [" + requeued.Priority + "] " + requeued.Name +
                Environment.NewLine + "Wykonanie: „kolejka przetworz”.";
        }

        // --- cron ---
        if (text.StartsWith("cron ", StringComparison.Ordinal))
        {
            int colon = text.IndexOf(':');
            if (colon < 0) return "Użyj: „cron opis: */15 * * * *” albo „cron nastepne: 0 8 * * 1-5”.";
            string expression = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            if (!CronSchedule.TryParse(expression, out var schedule, out string error) || schedule is null)
                return "Nie rozumiem tego crona: " + error;
            return "Cron „" + schedule.Expression + "” → " + schedule.Describe() + Environment.NewLine + schedule.DescribeNext(5);
        }

        // --- flagi funkcji ---
        if (text is "flagi" or "flagi funkcji" or "lista flag")
            return core.Flags.Describe();
        if (text.StartsWith("flaga ", StringComparison.Ordinal))
        {
            string[] parts = command.Trim()["flaga ".Length..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "Podaj nazwę flagi: „flaga eksperyment” albo „flaga eksperyment sesja-42”.";
            string? subject = parts.Length > 1 ? parts[1] : null;
            bool enabled = core.Flags.IsEnabled(parts[0], subject);
            var flag = core.Flags.Get(parts[0]);
            return flag is null
                ? "Nie ma flagi „" + parts[0] + "”. Nie wymyślam wartości — brak flagi znaczy „wyłączone”."
                : "Flaga „" + flag.Name + "”: " + (flag.Enabled ? "włączona" : "wyłączona") + " · zasięg " + flag.Percentage + "%" +
                  (subject is null ? " · bez podmiotu (grupa liczona z klucza „domyslny”)" : " · podmiot „" + subject + "”") +
                  " → " + (enabled ? "AKTYWNA" : "nieaktywna") + ".";
        }
        if (text.StartsWith("ustaw flage:", StringComparison.Ordinal) || text.StartsWith("ustaw flage ", StringComparison.Ordinal))
        {
            string payload = text.StartsWith("ustaw flage:", StringComparison.Ordinal)
                ? command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim()
                : command.Trim()["ustaw flage ".Length..].Trim();
            string[] parts = payload.Split([' ', '\t', '='], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "Użyj: „ustaw flage: eksperyment on”, „… off” albo „… 30” (procent).";
            string name = parts[0];
            string state = parts[1].ToLowerInvariant().TrimEnd('%');
            bool enabled;
            int percentage;
            if (state is "on" or "wlacz" or "wlaczona" or "true" or "100") { enabled = true; percentage = 100; }
            else if (state is "off" or "wylacz" or "wylaczona" or "false" or "0") { enabled = false; percentage = 0; }
            else if (int.TryParse(state, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed is >= 0 and <= 100) { enabled = parsed > 0; percentage = parsed; }
            else return "Stan flagi to „on”, „off” albo liczba 0–100 (procent użytkowników).";
            return core.Flags.Set(name, enabled, percentage, "ustawione poleceniem")
                ? "Flaga „" + name + "”: " + (enabled ? "włączona" : "wyłączona") + " · zasięg " + percentage + "%." +
                  (core.Flags.LastStorageError is null ? " Zapisano i sprawdzono odczytem zwrotnym." : " " + core.Flags.LastStorageError)
                : "Nie ustawiłem flagi — nazwa musi być jednym słowem bez spacji (max 120 znaków).";
        }

        // --- cache ---
        if (text.StartsWith("cache zapisz:", StringComparison.Ordinal))
        {
            string payload = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            int equals = payload.IndexOf('=');
            if (equals <= 0) return "Użyj: „cache zapisz: klucz = wartość”.";
            string key = payload[..equals].Trim();
            string value = payload[(equals + 1)..].Trim();
            core.Cache.Set(key, value, TimeSpan.FromMinutes(10));
            return "W cache: „" + key + "” (TTL 10 minut). " + core.Cache.Describe();
        }
        if (text.StartsWith("cache pokaz:", StringComparison.Ordinal))
        {
            string key = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            return core.Cache.TryGet(key, out string? value)
                ? "„" + key + "” = " + value
                : "Nie ma w cache klucza „" + key + "” (albo wygasł). " + core.Cache.Describe();
        }
        if (text.StartsWith("cache usun:", StringComparison.Ordinal))
        {
            string key = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            return core.Cache.Remove(key) ? "Usunięto z cache: „" + key + "”." : "Nie było w cache klucza „" + key + "”.";
        }

        // --- integralność plików ---
        if (text is "integralnosc" or "manifesty" or "integralnosc plikow")
            return DescribeManifests(core);
        if (text.StartsWith("integralnosc zbuduj:", StringComparison.Ordinal))
        {
            string target = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim().Trim('"');
            if (!Directory.Exists(target)) return "Nie ma katalogu: " + target;
            var entries = IntegrityManifest.Build(target);
            if (entries.Count == 0) return "Katalog jest pusty — nie buduję pustego manifestu.";
            string path = ManifestPath(core, target);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, IntegrityManifest.Serialize(entries));
            return "Manifest zapisany i sprawdzony odczytem zwrotnym." + Environment.NewLine +
                "· plik: " + path + Environment.NewLine + "· " + IntegrityManifest.Describe(entries);
        }
        if (text.StartsWith("integralnosc sprawdz:", StringComparison.Ordinal))
        {
            string target = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim().Trim('"');
            if (!Directory.Exists(target)) return "Nie ma katalogu: " + target;
            string path = ManifestPath(core, target);
            if (!File.Exists(path)) return "Nie mam manifestu dla tego katalogu. Najpierw: „integralnosc zbuduj: " + target + "”.";
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
            core.Log.Info("kopia.danych", "wykonano kopię danych");
            return report;
        }
        if (text.StartsWith("weryfikuj kopie:", StringComparison.Ordinal) || text.StartsWith("sprawdz kopie:", StringComparison.Ordinal))
        {
            string name = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            var backup = core.Backups.List().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? core.Backups.List().FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            return backup is null ? "Nie ma kopii o nazwie „" + name + "”. " + core.Backups.Describe() : core.Backups.Verify(backup);
        }

        // --- sejf na sekrety ---
        if (text is "sejf" or "sejf status" or "stan sejfu")
            return core.Vault.Describe();
        if (text is "sejf odblokuj" or "odblokuj sejf")
        {
            var passphrase = core.Vault.PassphraseProvider?.Invoke();
            if (passphrase is null)
                return "Nie przyjmuję hasła z treści polecenia — polecenia trafiają do historii i audytu, więc hasło w czacie jest hasłem ujawnionym. " +
                       "Użyj pola hasła w panelu Sejf (host przekazuje je bezpiecznym kanałem).";
            return core.Vault.Unlock(passphrase)
                ? "Sejf odblokowany. Wpisy odszyfrowane tylko w pamięci procesu."
                : "Nie odblokowałem sejfu: " + (core.Vault.LastError ?? "nieznany powód");
        }
        if (text is "sejf zablokuj" or "zablokuj sejf")
        {
            core.Vault.Lock();
            return "Sejf zablokowany, klucz wyzerowany w pamięci.";
        }
        if (text.StartsWith("sejf utworz", StringComparison.Ordinal))
        {
            var passphrase = core.Vault.PassphraseProvider?.Invoke();
            if (passphrase is null) return "Utworzenie sejfu wymaga hasła z pola w panelu (nie z czatu) — inaczej hasło zostałoby w historii poleceń.";
            if (core.Vault.Exists) return "Sejf już istnieje. Użyj „sejf odblokuj”.";
            return core.Vault.Create(passphrase) ? "Sejf utworzony (AES-256-GCM, klucz z PBKDF2)." : "Nie utworzyłem sejfu: " + core.Vault.LastError;
        }
        if (text.StartsWith("sejf dodaj:", StringComparison.Ordinal))
        {
            string payload = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            int equals = payload.IndexOf('=');
            if (equals <= 0) return "Użyj: „sejf dodaj: nazwa = wartość”. Uwaga: wartość w treści polecenia trafia do historii rozmowy — świadomie.";
            string name = payload[..equals].Trim();
            string secret = payload[(equals + 1)..].Trim();
            return core.Vault.Add(name, secret, "dodane poleceniem")
                ? "Zapisano sekret „" + name + "”. Wartości nie pokazuję w potwierdzeniu."
                : "Nie zapisałem sekretu: " + (core.Vault.LastError ?? "powód nieznany");
        }
        if (text.StartsWith("sejf pokaz:", StringComparison.Ordinal))
        {
            string name = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            string? secret = core.Vault.Get(name);
            return secret is null ? "Nie pokazuję sekretu: " + (core.Vault.LastError ?? "brak wpisu") : "Sekret „" + name + "”: " + secret;
        }
        if (text.StartsWith("sejf usun:", StringComparison.Ordinal))
        {
            string name = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            return core.Vault.Remove(name) ? "Usunięto wpis „" + name + "” z sejfu." : "Nie usunąłem: " + (core.Vault.LastError ?? "brak wpisu");
        }

        // --- maszyna stanów ---
        if (text.StartsWith("maszyna:", StringComparison.Ordinal) || text.StartsWith("maszyna ", StringComparison.Ordinal))
        {
            string target = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            if (target.Length == 0) return "Podaj stan docelowy, np. „maszyna: startuje”.";
            return core.Lifecycle.TryGo(target, out string reason, "polecenie użytkownika")
                ? "Przejście wykonane. " + Environment.NewLine + core.Lifecycle.Describe()
                : reason + Environment.NewLine + core.Lifecycle.Describe();
        }

        // --- workflow (DAG) ---
        if (text.StartsWith("workflow:", StringComparison.Ordinal) || text.StartsWith("graf:", StringComparison.Ordinal) ||
            text.StartsWith("dag:", StringComparison.Ordinal))
        {
            string spec = command.Trim()[(command.Trim().IndexOf(':') + 1)..].Trim();
            var graph = WorkflowGraph.Parse(spec, out string parseError);
            if (parseError.Length > 0) return parseError;
            return "Analiza grafu zadań (nic nie uruchamiam — to plan):" + Environment.NewLine + graph.Describe();
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
                string.Join(Environment.NewLine, values.Select(x => "· " + x.Key + " = " + Format(x.Value)));
        }

        return null;
    }

    private static string Format(object? value) => value switch
    {
        null => "(null)",
        byte[] bytes => bytes.Length + " bajtów",
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
        if (!Directory.Exists(directory)) return "Nie ma jeszcze żadnego manifestu. Zbuduj: „integralnosc zbuduj: <katalog>”.";
        var files = Directory.GetFiles(directory, "*.manifest.json");
        if (files.Length == 0) return "Katalog manifestów jest pusty.";
        var lines = new List<string> { "Zapisane manifesty (" + files.Length + "):" };
        lines.AddRange(files.Select(x => "· " + Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(x)) +
            " · " + new FileInfo(x).LastWriteTime.ToString("yyyy-MM-dd HH:mm") + " · " + new FileInfo(x).Length + " B"));
        lines.Add("Sprawdzenie: „integralnosc sprawdz: <katalog>”.");
        return string.Join(Environment.NewLine, lines);
    }
}
