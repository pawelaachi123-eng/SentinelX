using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Explicit, named, immutable snapshots protected for the current Windows account.</summary>
internal sealed class RagArchiveStore(string? directory = null)
{
    private readonly string root = directory ?? Path.Combine(AppPaths.Root, "RagArchives");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SentinelX.RAG.archive.v1");
    private const int MaxBytes = 64 * 1024 * 1024;
    internal static bool ValidName(string name) => Regex.IsMatch(name, @"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,59}\z")
        && !Regex.IsMatch(name, @"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase);
    private string FilePath(string name)
    {
        if (!ValidName(name)) throw new InvalidDataException("Nazwa: 1–60 liter ASCII, cyfr, _ lub -, bez ścieżki i nazw urządzeń Windows.");
        return Path.Combine(root, name + ".sxrag");
    }
    internal string List() => !Directory.Exists(root) ? "Archiwa RAG: 0." :
        "Archiwa RAG (szyfrowanie kontem Windows):\n" + string.Join("\n", Directory.EnumerateFiles(root, "*.sxrag")
            .Take(200).Select(Path.GetFileNameWithoutExtension).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    internal void Save(string name, KnowledgeRagService.Archive snapshot)
    {
        KnowledgeRagService.Validate(snapshot);
        string target = FilePath(name);
        if (File.Exists(target)) throw new IOException("Archiwum już istnieje. Wybierz nową nazwę; nie nadpisuję kopii.");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        byte[] encrypted;
        try
        {
            if (plain.Length > MaxBytes - 4096) throw new InvalidDataException("Archiwum przekracza limit 64 MiB.");
            encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Directory.CreateDirectory(root);
        string temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(encrypted); stream.Flush(true); }
            File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal KnowledgeRagService.Archive Load(string name)
    {
        using var stream = new FileStream(FilePath(name), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaxBytes) throw new InvalidDataException("Niepoprawny rozmiar archiwum.");
        byte[] encrypted = new byte[(int)stream.Length];
        stream.ReadExactly(encrypted);
        byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var snapshot = JsonSerializer.Deserialize<KnowledgeRagService.Archive>(plain)
                ?? throw new InvalidDataException("Archiwum jest puste.");
            KnowledgeRagService.Validate(snapshot);
            return snapshot;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
