using System.IO;

namespace SentinelX;

public sealed record RecentDownload(string Path, DateTimeOffset CompletedAt);

/// <summary>Tracks new stable files in Downloads while Sentinel is running; it never opens or executes them.</summary>
public sealed class DownloadContextService : IDisposable
{
    private static readonly HashSet<string> PartialExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".crdownload", ".part", ".partial", ".download", ".tmp" };
    private readonly object gate = new();
    private readonly string folder;
    private readonly Dictionary<string, Candidate> candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<RecentDownload> completed = new();
    private FileSystemWatcher? watcher;
    private DateTimeOffset lastPoll;
    private bool disposed;
    public bool IsAvailable => watcher != null;
    public event Action<RecentDownload>? Completed;

    public DownloadContextService(string? downloadsFolder = null)
    {
        folder = Path.GetFullPath(downloadsFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        try
        {
            if (!Directory.Exists(folder)) return;
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = 16 * 1024
            };
            watcher.Created += (_, e) => Note(e.FullPath);
            watcher.Changed += (_, e) => Note(e.FullPath);
            watcher.Renamed += (_, e) => { Note(e.OldFullPath); Note(e.FullPath); };
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { watcher?.Dispose(); watcher = null; }
    }

    public void Sample()
    {
        List<RecentDownload> justCompleted = [];
        lock (gate)
        {
            if (disposed || DateTimeOffset.UtcNow - lastPoll < TimeSpan.FromSeconds(2)) return;
            lastPoll = DateTimeOffset.UtcNow;
            foreach (string path in candidates.OrderBy(x => x.Value.LastEvent).Select(x => x.Key).ToArray())
            {
                var item = candidates[path];
                if (DateTimeOffset.UtcNow - item.LastEvent > TimeSpan.FromHours(24)) { candidates.Remove(path); continue; }
                try
                {
                    if (PartialExtensions.Contains(Path.GetExtension(path)) || !File.Exists(path)) continue;
                    var info = new FileInfo(path);
                    if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) { candidates.Remove(path); continue; }
                    if (info.Length == item.LastLength) item.StableSamples++; else { item.LastLength = info.Length; item.StableSamples = 0; }
                    if (item.StableSamples >= 2)
                    {
                        var result = new RecentDownload(path, DateTimeOffset.Now);
                        completed.Enqueue(result);
                        justCompleted.Add(result);
                        candidates.Remove(path);
                        while (completed.Count > 30) completed.Dequeue();
                    }
                    else candidates[path] = item;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }
        foreach (var item in justCompleted)
        {
            try { Completed?.Invoke(item); }
            catch (Exception ex) { AppLog.Write(ex); }
        }
    }

    /// <summary>Re-seeds bounded candidates from files changed since a persisted wait began while the app was closed.</summary>
    public int SeedRecentFilesSince(DateTimeOffset since, string? nameContains = null)
    {
        if (!IsAvailable) return 0;
        int seeded = 0;
        try
        {
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).Take(1000))
            {
                if (nameContains?.Length > 0 && !Path.GetFileName(path).Contains(nameContains, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var info = new FileInfo(path);
                    DateTime changedAt = info.LastWriteTimeUtc > info.CreationTimeUtc ? info.LastWriteTimeUtc : info.CreationTimeUtc;
                    if (changedAt < since.UtcDateTime || PartialExtensions.Contains(info.Extension) ||
                        (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) continue;
                    Note(path);
                    seeded++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return seeded;
    }

    public RecentDownload? GetMostRecent()
    {
        Sample();
        RecentDownload[] snapshot;
        lock (gate) snapshot = completed.ToArray();
        return snapshot.LastOrDefault(x => DateTimeOffset.Now - x.CompletedAt < TimeSpan.FromHours(24) &&
            File.Exists(x.Path) && IsWithin(x.Path, folder));
    }

    private void Note(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsWithin(path, folder)) return;
        lock (gate)
        {
            if (disposed) return;
            candidates[path] = new Candidate { LastEvent = DateTimeOffset.UtcNow, LastLength = -1, StableSamples = 0 };
            if (candidates.Count > 200)
            {
                string[] oldest = candidates.OrderBy(x => x.Value.LastEvent).Take(candidates.Count - 200).Select(x => x.Key).ToArray();
                foreach (string item in oldest) candidates.Remove(item);
            }
        }
    }

    private static bool IsWithin(string path, string root)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
        watcher?.Dispose();
    }

    private sealed class Candidate
    {
        public DateTimeOffset LastEvent;
        public long LastLength;
        public int StableSamples;
    }
}
