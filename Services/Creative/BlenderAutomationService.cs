using System.Diagnostics;
using System.Text;
using SentinelX.Services.History;

namespace SentinelX.Services.Creative;

public sealed record BlenderBuildResult(bool Available, bool Success, string Message, string Executable, string BlendPath, string FbxPath)
{
    public string Sha256Blend { get; init; } = "";
    public string Sha256Fbx { get; init; } = "";
    public int ExitCode { get; init; }
}

/// <summary>Runs the Blender executable with the generated scene script, no shell and no user-provided arguments.
/// It verifies the saved .blend and FBX files; it cannot certify visual quality or Roblox import compatibility.</summary>
public sealed class BlenderAutomationService
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);
    private const int MaximumCapturedOutputCharacters = 32 * 1024;
    private const long MaximumArtifactBytes = 250L * 1024 * 1024;

    public async Task<BlenderBuildResult> BuildSceneAsync(string generatedScriptPath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        string? executable = FindBlenderExecutable();
        if (executable == null)
            return new(false, false, "Blender nie został znaleziony. Źródłowy build_scene.py jest zapisany i można go uruchomić lokalnie po zainstalowaniu Blendera.", "", "", "");

        if (!TryResolveGeneratedScript(generatedScriptPath, out string scriptPath, out string outputDirectory, out string error))
            return new(true, false, "Odrzucono ścieżkę skryptu Blendera: " + error, executable, "", "");
        if (Directory.Exists(outputDirectory))
            return new(true, false, "Folder wynikowy Blendera już istnieje; generator nie nadpisuje scen ani assetów.", executable, "", "");
        Directory.CreateDirectory(outputDirectory);
        if ((File.GetAttributes(outputDirectory) & FileAttributes.ReparsePoint) != 0)
            return new(true, false, "Folder wynikowy Blendera jest dowiązaniem; nie wykonano skryptu.", executable, "", "");

        string blendPath = Path.Combine(outputDirectory, "game_blockout.blend");
        string fbxPath = Path.Combine(outputDirectory, "game_blockout.fbx");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(BuildTimeout);
        Process? process = null;
        string stdout = "";
        string stderr = "";
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath)!
            };
            start.ArgumentList.Add("--background");
            start.ArgumentList.Add("--factory-startup");
            start.ArgumentList.Add("--python");
            start.ArgumentList.Add(scriptPath);
            process = Process.Start(start);
            if (process == null) return new(true, false, "Windows nie uruchomił procesu Blendera.", executable, "", "");
            Task<string> stdoutTask = ReadOutputTailAsync(process.StandardOutput, timeout.Token);
            Task<string> stderrTask = ReadOutputTailAsync(process.StandardError, timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                return new(true, false, "Blender zakończył build kodem " + process.ExitCode + ".\n" + Tail(stderr, 3000), executable, blendPath, fbxPath) { ExitCode = process.ExitCode };
            if (!ValidArtifact(blendPath, 4096) || !ValidArtifact(fbxPath, 256))
                return new(true, false, "Blender zakończył proces, ale pliki .blend/FBX nie przeszły kontroli rozmiaru i istnienia.\n" + Tail(stdout + "\n" + stderr, 2500), executable, blendPath, fbxPath) { ExitCode = process.ExitCode };

            string blendHash = HashFile(blendPath);
            string fbxHash = HashFile(fbxPath);
            string evidence = $"Blender executable: {executable}\nBlend: {blendPath} · {new FileInfo(blendPath).Length} bajtów · SHA-256 {blendHash}\nFBX: {fbxPath} · {new FileInfo(fbxPath).Length} bajtów · SHA-256 {fbxHash}\nProces zakończył się kodem 0. To nie potwierdza poprawności importu ani wyglądu w Roblox Studio.";
            ActionEvidenceCapture.Record(new ActionHistoryEntry
            {
                ActionId = "SX-BLENDER-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                Timestamp = DateTime.Now,
                ActionType = "BLENDER_ASSET_BUILD",
                Command = "Wygeneruj lokalną scenę Blender dla projektu Roblox",
                Status = "VERIFIED",
                Message = "Blender zakończył generowanie sceny i eksport FBX; pliki przeszły kontrolę rozmiaru i SHA-256.",
                Evidence = evidence,
                RecoveryAdvice = "Otwórz .blend i obejrzyj scenę; przed importem FBX do Roblox zweryfikuj skalę, orientację, materiały i liczbę trójkątów."
            });
            return new(true, true, "Blender wygenerował kopię sceny .blend i asset FBX. Obejrzyj scenę przed importem do Roblox.", executable, blendPath, fbxPath)
            {
                Sha256Blend = blendHash,
                Sha256Fbx = fbxHash,
                ExitCode = process.ExitCode
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            KillTree(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            return new(true, false, "Przekroczono limit czasu 3 minut. Proces Blendera został zatrzymany; sprawdź niekompletne pliki w folderze output.", executable, blendPath, fbxPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            KillTree(process);
            return new(true, false, "Nie udało się bezpiecznie uruchomić Blendera: " + ex.Message, executable, blendPath, fbxPath);
        }
        finally { process?.Dispose(); }
    }

    public ActionExecutionResult OpenGeneratedScene(string generatedBlendPath)
    {
        string? executable = FindBlenderExecutable();
        if (executable == null) return ActionExecutionResult.Failure("Blender nie został znaleziony; scena .blend pozostała zapisana.");
        if (!TryResolveGeneratedArtifact(generatedBlendPath, ".blend", out string blendPath, out string error))
            return ActionExecutionResult.Failure("Odrzucono plik sceny: " + error);
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            start.ArgumentList.Add(blendPath);
            using var process = Process.Start(start);
            if (process == null) return ActionExecutionResult.Failure("Proces Blendera nie został uruchomiony.");
            return ActionExecutionResult.UnverifiedSuccess("Wysłano do Blendera wygenerowaną scenę.",
                "Proces startowy został utworzony dla pliku " + blendPath + ". Nie zweryfikowano widoczności okna ani załadowanej sceny.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return ActionExecutionResult.Failure("Nie udało się otworzyć sceny w Blenderze.", ex.Message); }
    }

    public static string? FindBlenderExecutable()
    {
        var candidates = new List<string>();
        string? explicitPath = Environment.GetEnvironmentVariable("SENTINEL_BLENDER_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath)) candidates.Add(explicitPath.Trim().Trim('"'));
        if (OperatingSystem.IsWindows())
        {
            foreach (Environment.SpecialFolder special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
            {
                string root = Environment.GetFolderPath(special);
                string blenderRoot = special == Environment.SpecialFolder.LocalApplicationData
                    ? Path.Combine(root, "Programs", "Blender Foundation")
                    : Path.Combine(root, "Blender Foundation");
                try
                {
                    if (Directory.Exists(blenderRoot))
                        foreach (string version in Directory.EnumerateDirectories(blenderRoot, "Blender*", SearchOption.TopDirectoryOnly))
                            candidates.Add(Path.Combine(version, "blender.exe"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        else
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                candidates.Add(Path.Combine(folder, "blender"));
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string full = Path.GetFullPath(candidate);
                if (!File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) continue;
                string product = FileVersionInfo.GetVersionInfo(full).ProductName ?? "";
                if (product.Contains("Blender", StringComparison.OrdinalIgnoreCase)) return full;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        }
        return null;
    }

    private static bool TryResolveGeneratedScript(string input, out string scriptPath, out string outputDirectory, out string error)
    {
        scriptPath = outputDirectory = error = "";
        try
        {
            scriptPath = Path.GetFullPath(input ?? "");
            if (!File.Exists(scriptPath) || !string.Equals(Path.GetFileName(scriptPath), "build_scene.py", StringComparison.OrdinalIgnoreCase))
            { error = "oczekiwano wygenerowanego pliku build_scene.py."; return false; }
            if ((File.GetAttributes(scriptPath) & FileAttributes.ReparsePoint) != 0)
            { error = "skrypt jest dowiązaniem systemu plików."; return false; }
            string project = Directory.GetParent(Path.GetDirectoryName(scriptPath)!)?.Parent?.FullName ?? "";
            if (Path.GetFileName(Path.GetDirectoryName(scriptPath)) != "blender" || !Path.GetFileName(project).StartsWith("sentinel-roblox-", StringComparison.Ordinal))
            { error = "skrypt nie znajduje się w nowym folderze projektu Sentinel."; return false; }
            outputDirectory = Path.Combine(Path.GetDirectoryName(scriptPath)!, "output");
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { error = ex.Message; return false; }
    }

    private static bool TryResolveGeneratedArtifact(string input, string extension, out string path, out string error)
    {
        path = error = "";
        try
        {
            path = Path.GetFullPath(input ?? "");
            if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) || !ValidArtifact(path, 4096))
            { error = "plik nie istnieje, ma nieprawidłowe rozszerzenie lub rozmiar."; return false; }
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            { error = "plik jest dowiązaniem systemu plików."; return false; }
            string? output = Directory.GetParent(path)?.FullName;
            if (output == null || Path.GetFileName(output) != "output" || Path.GetFileName(Directory.GetParent(output)?.FullName) != "blender")
            { error = "plik nie znajduje się w folderze output wygenerowanej sceny."; return false; }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { error = ex.Message; return false; }
    }

    private static bool ValidArtifact(string path, long minimumBytes)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length >= minimumBytes && info.Length <= MaximumArtifactBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>Continuously drains child-process output while retaining only its tail, preventing an unexpectedly noisy Blender process from exhausting memory.</summary>
    private static async Task<string> ReadOutputTailAsync(StreamReader reader, CancellationToken token)
    {
        var tail = new StringBuilder(MaximumCapturedOutputCharacters);
        char[] buffer = new char[4096];
        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                if (read == 0) break;
                tail.Append(buffer, 0, read);
                if (tail.Length > MaximumCapturedOutputCharacters)
                    tail.Remove(0, tail.Length - MaximumCapturedOutputCharacters);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        return tail.ToString();
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static string Tail(string text, int maximum) => text.Length <= maximum ? text : text[^maximum..];
    private static void KillTree(Process? process)
    {
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }
}
