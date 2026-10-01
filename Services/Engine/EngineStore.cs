namespace SentinelX.Services.Engine;

/// <summary>Where the engine lives on disk. Nothing here needs the user to touch it.
/// Bundled runtime (shipped inside the installer) is preferred; otherwise it is downloaded once into %LOCALAPPDATA%.</summary>
public sealed class EngineStore
{
    public EngineStore(string? root = null, string? bundledDirectory = null)
    {
        Root = root ?? Path.Combine(AppPaths.Root, "Engine");
        BundledDirectory = bundledDirectory ?? Path.Combine(AppContext.BaseDirectory, "Engine", "llama");
    }

    public string Root { get; }
    public string BundledDirectory { get; }
    public string ModelsDirectory => Path.Combine(Root, "models");
    public string DownloadsDirectory => Path.Combine(Root, "downloads");
    public string RuntimeDirectory => Path.Combine(Root, "llama-" + EngineCatalog.LlamaBuild);

    public string ModelPath(EngineModelSpec spec) => Path.Combine(ModelsDirectory, spec.FileName);

    /// <summary>A model file only appears under its final name after its SHA-256 was verified (downloads go to *.part first),
    /// so existence plus the exact size is enough to call it installed.</summary>
    public bool IsModelInstalled(EngineModelSpec spec)
    {
        try
        {
            string path = ModelPath(spec);
            return File.Exists(path) && new FileInfo(path).Length == spec.SizeBytes;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public IReadOnlyList<EngineModelSpec> InstalledModels() => EngineCatalog.Models.Where(IsModelInstalled).ToArray();

    public long PartialBytes(EngineModelSpec spec)
    {
        try
        {
            string part = ModelPath(spec) + ".part";
            return File.Exists(part) ? new FileInfo(part).Length : 0;
        }
        catch (IOException) { return 0; }
    }

    public string? FindServerExecutable()
    {
        foreach (string directory in new[] { BundledDirectory, RuntimeDirectory })
        {
            string? found = FindIn(directory);
            if (found != null) return found;
        }
        return null;
    }

    private static string? FindIn(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return null;
            return Directory.EnumerateFiles(directory, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public long FreeBytes()
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(Root));
            return string.IsNullOrEmpty(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return long.MaxValue; }
    }

    public void DeleteModel(EngineModelSpec spec)
    {
        try { File.Delete(ModelPath(spec)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
    }
}
