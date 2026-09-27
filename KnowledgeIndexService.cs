using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 8 — indeks plików tekstowych BEZ embeddingów i BEZ sieci: skan .txt/.md z podanego
/// folderu (do 200 plików, głębokość 3, 2 MB na plik), słowa treściowe (bez stopwordów, bez
/// polskich znaków po redukcji) mapowane na nazwy plików. Indeks żyje TYLKO w RAM procesu —
/// nic nie zapisuje na dysk i nic nie wysyła; po zamknięciu aplikacji znika.
/// </summary>
public static class KnowledgeIndexService
{
    private static readonly object Sync = new();
    private static readonly string[] Extensions = [".txt", ".md"];
    private static readonly string[] StopWords =
    {
        "the","and","for","that","with","this","jest","ktory","ktora","ktore","oraz","nie","jak","przez","bardzo",
        "wszystko","ktora","moze","tym","tej","tego","temu","jako","takze","bedzie","ktore",
    };

    private static Dictionary<string, List<string>> words = new(StringComparer.Ordinal);
    private static string sourceRoot = "";
    private static int indexedFiles;
    private static DateTime builtAt;

    /// <summary>Routing poleceń indeksu (testowalny bez pełnego routera).</summary>
    public static string? TryHandleCommand(string command, string text)
    {
        if (text is "indeks status" or "status indeksu") return Status();
        if (text is "indeks zbuduj" or "indeks buduj")
            return "Podaj folder: „indeks zbuduj: C:\\dokumenty”. Skanuję .txt i .md (do 200 plików, głębokość 3) — indeks powstaje tylko w RAM.";
        if (text is "indeks szukaj" or "indeks znajdz")
            return "Podaj frazę: „indeks szukaj: prywatność”. Najpierw zbuduj indeks: „indeks zbuduj: folder”.";
        var build = Regex.Match(command, @"^indeks (?:zbuduj|buduj)[:\s]+(.+)$", RegexOptions.IgnoreCase);
        if (build.Success) return Build(build.Groups[1].Value);
        var search = Regex.Match(command, @"^indeks (?:szukaj|znajdz)[:\s]+(.+)$", RegexOptions.IgnoreCase);
        if (search.Success) return Search(search.Groups[1].Value);
        return null;
    }

    public static string Build(string directory)
    {
        string dir = (directory ?? "").Trim().Trim('"');
        if (dir.Length == 0) return "Podaj folder, np. „indeks zbuduj: C:\\dokumenty”.";
        if (!Directory.Exists(dir)) return "Folder nie istnieje: " + dir + " — sprawdzam dokładnie tę ścieżkę, nie zgaduję.";
        var files = new List<string>();
        try
        {
            foreach (string file in Enumerate(dir, 0))
            {
                files.Add(file);
                if (files.Count >= 200) break;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return "Nie mogę czytać folderu („" + ex.Message + "”) — indeksu nie buduję na częściowych danych.";
        }
        if (files.Count == 0)
            return "W folderze nie ma plików .txt ani .md (szukałem do głębokości 3). Indeks zostaje bez zmian.";

        var fresh = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            try
            {
                if (new FileInfo(file).Length > 2_000_000) continue;
                string content = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(content.ToLowerInvariant(), "[a-ząćęłńóśźż0-9]{3,}"))
                {
                    string token = Deaccent(m.Value);
                    if (token.Length < 3 || StopWords.Contains(token)) continue;
                    if (!fresh.TryGetValue(token, out var list)) fresh[token] = list = [];
                    string name = Path.GetFileName(file);
                    if (!list.Contains(name)) list.Add(name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* pojedynczy plik pomijam */ }
        }
        lock (Sync)
        {
            words = fresh;
            sourceRoot = dir;
            indexedFiles = files.Count;
            builtAt = DateTime.Now;
        }
        return "INDEKS ZBUDOWANY: " + files.Count + " plików, " + fresh.Count + " unikalnych słów treściowych (źródło: " + dir + ")" + Environment.NewLine +
            "· indeks żyje TYLKO w RAM aplikacji — po zamknięciu znika, na dysk nie zapisuję nic" + Environment.NewLine +
            "· szukaj: „indeks szukaj: fraza” · stan: „indeks status”";
    }

    public static string Search(string query)
    {
        lock (Sync)
        {
            if (words.Count == 0)
                return "Indeks jest pusty — najpierw „indeks zbuduj: folder” z plikami .txt/.md. Niczego nie zgaduję bez indeksu.";
            var scores = new Dictionary<string, int>(StringComparer.Ordinal);
            int usedTokens = 0;
            foreach (Match m in Regex.Matches((query ?? "").ToLowerInvariant(), "[a-ząćęłńóśźż0-9]{3,}"))
            {
                string token = Deaccent(m.Value);
                if (token.Length < 3 || StopWords.Contains(token)) continue;
                usedTokens++;
                if (words.TryGetValue(token, out var list))
                    foreach (string name in list)
                        scores[name] = scores.GetValueOrDefault(name) + 1;
            }
            if (usedTokens == 0)
                return "Fraza po odjęciu słów nieważnych jest pusta — podaj treściwe słowo (min. 3 znaki).";
            if (scores.Count == 0)
                return "Brak trafień w indeksowanych plikach (" + indexedFiles + " plików z „" + sourceRoot + "”). Słowa z zapytania nie występują w indeksie.";
            var sb = new StringBuilder("WYNIKI (słowa z zapytania: " + usedTokens + ", trafione pliki: " + scores.Count + "):").AppendLine();
            foreach (var pair in scores.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(5))
                sb.Append("· ").Append(pair.Key).Append(" (trafień: ").Append(pair.Value).Append(")").AppendLine();
            sb.Append("· plik czytasz u siebie — Sentinel pokazuje tylko nazwy, nie otwiera plików");
            return sb.ToString();
        }
    }

    public static string Status()
    {
        lock (Sync)
        {
            if (words.Count == 0)
                return "Indeks jest pusty (nic nie zbudowano w tej sesji). Budowa: „indeks zbuduj: folder” — tylko RAM, bez zapisu na dysk.";
            return "INDEKS: " + indexedFiles + " plików, " + words.Count + " unikalnych słów, źródło: " + sourceRoot +
                ", zbudowano " + builtAt.ToString("HH:mm:ss", CultureInfo.GetCultureInfo("pl-PL")) + Environment.NewLine +
                "· tylko w RAM tej sesji · szukaj: „indeks szukaj: fraza”";
        }
    }

    /// <summary>Wspólna, limitowana enumeracja plików tekstowych (używa też RAG).</summary>
    internal static IEnumerable<string> CollectTextFiles(string directory, int max)
    {
        var files = new List<string>();
        foreach (string file in Enumerate(directory, 0))
        {
            files.Add(file);
            if (files.Count >= max) break;
        }
        return files;
    }

    private static IEnumerable<string> Enumerate(string directory, int depth)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
            if (Extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                yield return file;
        if (depth >= 3) yield break;
        foreach (string sub in Directory.EnumerateDirectories(directory))
            foreach (string file in Enumerate(sub, depth + 1))
                yield return file;
    }

    private static string Deaccent(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
            sb.Append(c switch { 'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n', 'ó' => 'o', 'ś' => 's', 'ź' => 'z', 'ż' => 'z', _ => c });
        return sb.ToString();
    }
}
