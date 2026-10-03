using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX.Services.Link;

/// <summary>
/// Bounded, owner-scoped phone transfers. Files are stored only below Sentinel X's dedicated PhoneTransfers/Files directory;
/// client-supplied names are display metadata and are never used as paths.
/// </summary>
internal sealed class PhoneFileTransferService : IDisposable
{
    internal const int ChunkBytes = 40 * 1024;
    internal const long MaxFileBytes = 25L * 1024 * 1024;
    private const int MaxFiles = 1000;
    private const int MaxActiveUploads = 3;
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromHours(1);
    private static readonly Regex ValidId = new("^[a-f0-9]{32}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ValidHash = new("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed class Manifest
    {
        public int Version { get; set; } = 1;
        public List<FileEntry> Files { get; set; } = [];
    }

    private sealed class FileEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
        public DateTimeOffset UploadedAt { get; set; }
        public string OwnerDeviceId { get; set; } = "";

        public LinkFileRecord ToWire() => new(Id, Name, Size, Sha256, UploadedAt);
    }

    private sealed class Upload : IDisposable
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Sha256 { get; init; }
        public required byte[] ExpectedHash { get; init; }
        public required string OwnerDeviceId { get; init; }
        public required string PartialPath { get; init; }
        public required FileStream Stream { get; init; }
        public IncrementalHash Hash { get; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public long Size { get; init; }
        public long Written { get; set; }
        public int NextIndex { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        public void Dispose()
        {
            try { Stream.Dispose(); } catch (IOException) { }
            Hash.Dispose();
        }
    }

    private readonly object gate = new();
    private readonly string root;
    private readonly string filesDirectory;
    private readonly string manifestPath;
    private readonly Dictionary<string, Upload> uploads = new(StringComparer.Ordinal);
    private List<FileEntry> files = [];
    private bool disposed;
    private bool persistenceBlocked;

    public string? LastError { get; private set; }
    public string FilesDirectory => filesDirectory;

    public PhoneFileTransferService(string? directory = null)
    {
        root = Path.Combine(directory ?? AppPaths.Root, "PhoneTransfers");
        filesDirectory = Path.Combine(root, "Files");
        manifestPath = Path.Combine(root, "index.json");
        Load();
        RemoveStalePartials();
    }

    public IReadOnlyList<LinkFileRecord> List(string ownerDeviceId)
    {
        lock (gate)
            return files.Where(x => x.OwnerDeviceId == ownerDeviceId)
                .OrderByDescending(x => x.UploadedAt).Select(x => x.ToWire()).ToArray();
    }

    public bool TryBegin(string ownerDeviceId, string? name, long size, string? sha256,
        out LinkFileUploadStarted? started, out int status, out string error)
    {
        started = null;
        status = 400;
        error = "";
        string safeName = (name ?? "").Trim();
        string hash = (sha256 ?? "").Trim();
        if (ownerDeviceId.Length is < 1 or > 40 || ownerDeviceId.Any(char.IsControl))
        { error = "Nieprawidłowe urządzenie."; return false; }
        if (!IsValidName(safeName)) { error = "Podaj zwykłą nazwę pliku bez ścieżki ani znaków sterujących."; return false; }
        if (size is < 1 or > MaxFileBytes) { error = "Plik musi mieć od 1 bajta do 25 MiB."; return false; }
        if (!ValidHash.IsMatch(hash)) { error = "Nieprawidłowy skrót SHA-256 pliku."; return false; }

        lock (gate)
        {
            if (disposed) { status = 503; error = "Transfer plików jest wyłączony."; return false; }
            PruneStaleUploadsLocked(DateTimeOffset.UtcNow);
            if (persistenceBlocked) { status = 503; error = LastError ?? "Magazyn plików jest tylko do odczytu."; return false; }
            if (files.Count >= MaxFiles) { status = 429; error = "Osiągnięto limit 1000 plików. Usuń stare pliki z panelu telefonu."; return false; }
            if (uploads.Values.Count(x => x.OwnerDeviceId == ownerDeviceId) >= MaxActiveUploads)
            { status = 429; error = "Dla tego telefonu trwa już maksymalna liczba transferów."; return false; }

            try
            {
                Directory.CreateDirectory(filesDirectory);
                string id = Guid.NewGuid().ToString("N");
                string partialPath = Path.Combine(root, id + ".part");
                var upload = new Upload
                {
                    Id = id,
                    Name = safeName,
                    Sha256 = hash.ToLowerInvariant(),
                    ExpectedHash = Convert.FromHexString(hash),
                    OwnerDeviceId = ownerDeviceId,
                    PartialPath = partialPath,
                    Stream = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        ChunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan),
                    Size = size,
                };
                uploads.Add(id, upload);
                started = new LinkFileUploadStarted(id, ChunkBytes);
                status = 200;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                LastError = "Nie udało się rozpocząć transferu: " + ex.Message;
                status = 503;
                error = LastError;
                return false;
            }
        }
    }

    public async Task<LinkFileChunkResult> WriteChunkAsync(string ownerDeviceId, string? id, int index,
        byte[]? bytes, CancellationToken cancellationToken)
    {
        if (id == null || !ValidId.IsMatch(id)) return new(false, false, 0, null, "Nieprawidłowy identyfikator transferu.", 400);
        Upload? upload;
        lock (gate)
        {
            if (disposed) return new(false, false, 0, null, "Transfer plików jest wyłączony.", 503);
            if (!uploads.TryGetValue(id, out upload)) return new(false, false, 0, null, "Transfer wygasł lub nie istnieje.", 404);
        }

        await upload.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (!uploads.TryGetValue(id, out Upload? current) || !ReferenceEquals(current, upload))
                    return new(false, false, 0, null, "Transfer został anulowany.", 404);
            }
            if (!string.Equals(upload.OwnerDeviceId, ownerDeviceId, StringComparison.Ordinal))
                return new(false, false, upload.NextIndex, null, "Transfer należy do innego telefonu.", 404);
            if (index != upload.NextIndex)
                return new(false, false, upload.NextIndex, null, "Porcja powtórzona lub poza kolejnością; ponów od wskazanego indeksu.", 409);
            if (bytes == null || bytes.Length == 0 || bytes.Length > ChunkBytes)
                return new(false, false, upload.NextIndex, null, "Nieprawidłowy rozmiar porcji.", 400);
            int required = (int)Math.Min(ChunkBytes, upload.Size - upload.Written);
            if (required <= 0 || bytes.Length != required)
                return new(false, false, upload.NextIndex, null, "Rozmiar porcji nie zgadza się z deklarowanym rozmiarem pliku.", 400);

            await upload.Stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            upload.Hash.AppendData(bytes);
            upload.Written += bytes.Length;
            upload.NextIndex++;
            upload.UpdatedAt = DateTimeOffset.UtcNow;

            if (upload.Written < upload.Size)
                return new(true, false, upload.NextIndex, null, "");

            await upload.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            upload.Stream.Flush(flushToDisk: true);
            byte[] actualHash = upload.Hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(actualHash, upload.ExpectedHash))
            {
                RemoveUpload(upload, deletePartial: true);
                return new(false, false, upload.NextIndex, null, "Weryfikacja SHA-256 nie powiodła się; nie zapisano pliku.", 422);
            }

            upload.Stream.Dispose();
            string finalPath = FilePath(upload.Id);
            var entry = new FileEntry
            {
                Id = upload.Id, Name = upload.Name, Size = upload.Size, Sha256 = upload.Sha256,
                UploadedAt = DateTimeOffset.UtcNow, OwnerDeviceId = upload.OwnerDeviceId,
            };
            lock (gate)
            {
                if (disposed || !uploads.TryGetValue(id, out Upload? current) || !ReferenceEquals(current, upload))
                {
                    TryDelete(upload.PartialPath);
                    upload.Hash.Dispose();
                    return new(false, false, upload.NextIndex, null, "Transfer został anulowany.", 404);
                }
                if (files.Count >= MaxFiles)
                {
                    uploads.Remove(id);
                    TryDelete(upload.PartialPath);
                    upload.Hash.Dispose();
                    return new(false, false, upload.NextIndex, null, "Osiągnięto limit 1000 plików.", 429);
                }
                try
                {
                    File.Move(upload.PartialPath, finalPath, overwrite: false);
                    var next = files.Append(entry).ToList();
                    if (!Persist(next))
                    {
                        TryDelete(finalPath);
                        uploads.Remove(id);
                        upload.Hash.Dispose();
                        return new(false, false, upload.NextIndex, null, LastError ?? "Nie udało się zapisać historii plików.", 503);
                    }
                    files = next;
                    uploads.Remove(id);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    uploads.Remove(id);
                    TryDelete(upload.PartialPath);
                    upload.Hash.Dispose();
                    LastError = "Nie udało się zachować odebranego pliku: " + ex.Message;
                    return new(false, false, upload.NextIndex, null, LastError, 503);
                }
            }
            upload.Hash.Dispose();
            return new(true, true, upload.NextIndex, entry.ToWire(), "");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            RemoveUpload(upload, deletePartial: true);
            LastError = "Transfer pliku został przerwany: " + ex.Message;
            return new(false, false, upload.NextIndex, null, LastError, 503);
        }
        finally
        {
            try { upload.Gate.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    public async Task<bool> CancelAsync(string ownerDeviceId, string? id, CancellationToken cancellationToken)
    {
        if (id == null || !ValidId.IsMatch(id)) return false;
        Upload? upload;
        lock (gate)
        {
            if (!uploads.TryGetValue(id, out upload) || upload.OwnerDeviceId != ownerDeviceId) return false;
        }
        await upload.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (!uploads.TryGetValue(id, out Upload? current) || !ReferenceEquals(current, upload)) return false;
                uploads.Remove(id);
            }
            upload.Dispose();
            TryDelete(upload.PartialPath);
            return true;
        }
        finally
        {
            try { upload.Gate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    public bool TryGetDownload(string ownerDeviceId, string? id, out LinkFileRecord? file, out FileStream? stream)
    {
        file = null;
        stream = null;
        if (id == null || !ValidId.IsMatch(id)) return false;
        lock (gate)
        {
            FileEntry? entry = files.FirstOrDefault(x => x.Id == id && x.OwnerDeviceId == ownerDeviceId);
            if (entry == null) return false;
            try
            {
                stream = new FileStream(FilePath(entry.Id), FileMode.Open, FileAccess.Read, FileShare.Read,
                    ChunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length != entry.Size)
                {
                    stream.Dispose();
                    stream = null;
                    LastError = "Rozmiar pliku na dysku nie zgadza się z historią transferów.";
                    return false;
                }
                file = entry.ToWire();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastError = "Nie udało się otworzyć pliku: " + ex.Message;
                return false;
            }
        }
    }

    public bool TryDelete(string ownerDeviceId, string? id, out string error)
    {
        error = "";
        if (id == null || !ValidId.IsMatch(id)) { error = "Nieprawidłowy identyfikator pliku."; return false; }
        lock (gate)
        {
            FileEntry? entry = files.FirstOrDefault(x => x.Id == id && x.OwnerDeviceId == ownerDeviceId);
            if (entry == null) { error = "Nie znaleziono pliku na tym telefonie."; return false; }
            var next = files.Where(x => x.Id != id).ToList();
            if (!Persist(next)) { error = LastError ?? "Nie udało się zaktualizować historii plików."; return false; }
            try
            {
                File.Delete(FilePath(id));
                files = next;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Restore the metadata if Windows cannot remove a file currently in use.
                if (Persist(files)) { }
                error = "Nie udało się usunąć pliku (może być otwarty): " + ex.Message;
                return false;
            }
        }
    }

    public async Task CancelOwnerAsync(string ownerDeviceId, CancellationToken cancellationToken = default)
    {
        string[] ids;
        lock (gate) ids = uploads.Values.Where(x => x.OwnerDeviceId == ownerDeviceId).Select(x => x.Id).ToArray();
        foreach (string id in ids) await CancelAsync(ownerDeviceId, id, cancellationToken).ConfigureAwait(false);
    }

    private void RemoveUpload(Upload upload, bool deletePartial)
    {
        lock (gate)
        {
            if (uploads.TryGetValue(upload.Id, out Upload? current) && ReferenceEquals(current, upload)) uploads.Remove(upload.Id);
        }
        upload.Dispose();
        if (deletePartial) TryDelete(upload.PartialPath);
    }

    private void PruneStaleUploadsLocked(DateTimeOffset now)
    {
        foreach (Upload upload in uploads.Values.Where(x => now - x.UpdatedAt > UploadLifetime).ToArray())
        {
            if (!uploads.Remove(upload.Id)) continue;
            upload.Dispose();
            TryDelete(upload.PartialPath);
        }
    }

    private void RemoveStalePartials()
    {
        try
        {
            if (!Directory.Exists(root)) return;
            DateTime cutoff = DateTime.UtcNow.Subtract(UploadLifetime).AddMinutes(-5);
            foreach (string path in Directory.EnumerateFiles(root, "*.part", SearchOption.TopDirectoryOnly))
            {
                try { if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private bool Persist(List<FileEntry> candidate)
    {
        if (persistenceBlocked) return false;
        string temp = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(root);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Manifest { Files = candidate }, LinkJson.Options);
            File.WriteAllBytes(temp, bytes);
            Manifest check = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(temp), LinkJson.Options)
                ?? throw new InvalidDataException("Pusta historia plików.");
            if (check.Version != 1 || check.Files.Count != candidate.Count
                || check.Files.Where((x, i) => x.Id != candidate[i].Id || x.Name != candidate[i].Name
                    || x.Size != candidate[i].Size || x.Sha256 != candidate[i].Sha256
                    || x.UploadedAt != candidate[i].UploadedAt || x.OwnerDeviceId != candidate[i].OwnerDeviceId).Any())
                throw new InvalidDataException("Historia plików nie przeszła odczytu kontrolnego.");
            File.Move(temp, manifestPath, overwrite: true);
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            LastError = "Nie udało się zapisać historii transferów: " + ex.Message;
            AppLog.Write("Files", "Error", "The phone file-transfer index could not be saved.", ex);
            return false;
        }
        finally { TryDelete(temp); }
    }

    private void Load()
    {
        if (!File.Exists(manifestPath)) return;
        try
        {
            if (new FileInfo(manifestPath).Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Historia transferów przekracza limit 4 MiB.");
            Manifest loaded = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(manifestPath), LinkJson.Options)
                ?? throw new InvalidDataException("Nieprawidłowa historia transferów.");
            if (loaded.Version != 1 || loaded.Files.Count > MaxFiles || loaded.Files.Any(x => !IsValidEntry(x))
                || loaded.Files.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != loaded.Files.Count)
                throw new InvalidDataException("Historia transferów zawiera wpisy nieprawidłowe lub powtórzone.");
            files = loaded.Files.Where(x => File.Exists(FilePath(x.Id)) && new FileInfo(FilePath(x.Id)).Length == x.Size).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            string quarantine = manifestPath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                File.Move(manifestPath, quarantine, false);
                LastError = "Historia transferów była uszkodzona i została zachowana jako " + Path.GetFileName(quarantine) + ".";
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                persistenceBlocked = true;
                LastError = "Nie można zachować uszkodzonej historii plików; nowe transfery są zablokowane.";
            }
            AppLog.Write("Files", "Warning", "The phone transfer index could not be loaded safely.", ex);
        }
    }

    private static bool IsValidEntry(FileEntry? entry) => entry != null && ValidId.IsMatch(entry.Id)
        && IsValidName(entry.Name) && entry.Size is > 0 and <= MaxFileBytes && ValidHash.IsMatch(entry.Sha256)
        && entry.UploadedAt != default && entry.OwnerDeviceId.Length is > 0 and <= 40 && !entry.OwnerDeviceId.Any(char.IsControl);

    private static bool IsValidName(string name)
    {
        if (name.Length is < 1 or > 180 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')
            || name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0
            || name.Any(char.IsControl)) return false;
        string stem = name.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL")
            && !Regex.IsMatch(stem, "^(COM|LPT)[0-9]$", RegexOptions.CultureInvariant);
    }

    private string FilePath(string id) => Path.Combine(filesDirectory, id + ".bin");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        Upload[] active;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            active = uploads.Values.ToArray();
            uploads.Clear();
        }
        foreach (Upload upload in active)
        {
            upload.Dispose();
            TryDelete(upload.PartialPath);
        }
    }
}
