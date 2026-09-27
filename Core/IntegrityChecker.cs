using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SentinelX;

/// <summary>0.97 · SHA-256 integrity check over Sentinel's own data files (#022). Read-only: it never repairs anything by itself.</summary>
public static class IntegrityChecker
{
    public sealed record Entry(string Name, long Bytes, string Sha256, bool Exists, string Status);

    public static IReadOnlyList<Entry> Check(IEnumerable<string> files)
    {
        var entries = new List<Entry>();
        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file);
            if (!File.Exists(file)) { entries.Add(new Entry(name, 0, "", false, "brak pliku — powstanie przy pierwszym zapisie")); continue; }
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                entries.Add(new Entry(name, bytes.Length, hash, true, bytes.Length > 0 ? "ok" : "pusty (0 bajtów)"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                entries.Add(new Entry(name, 0, "", true, "nie da się odczytać: " + ex.Message));
            }
        }
        return entries;
    }

    public static string Describe(IReadOnlyList<Entry> entries)
    {
        var builder = new StringBuilder();
        int readable = entries.Count(e => e.Exists && e.Sha256.Length > 0);
        builder.AppendLine("SPÓJNOŚĆ DANYCH · " + readable + "/" + entries.Count + " plików odczytanych i policzonych (SHA-256)");
        foreach (Entry entry in entries)
        {
            string size = entry.Bytes > 0 ? (entry.Bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB" : "—";
            string hash = entry.Sha256.Length >= 16 ? entry.Sha256[..16] + "…" : "—";
            builder.AppendLine("· " + entry.Name + " — " + size + " — " + hash + " — " + entry.Status);
        }
        builder.AppendLine();
        builder.AppendLine("To tylko odczyt i suma kontrolna: Sentinel niczego nie naprawia sam, bo nie wiemy, która wersja jest prawdziwa.");
        builder.AppendLine("Jeśli plik jest uszkodzony, przy następnym starcie zrobi się kopia „.damaged”, a dane odtworzy się z kopii zapasowej: backup.");
        return builder.ToString().TrimEnd();
    }
}
