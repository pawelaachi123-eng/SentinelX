using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;


namespace SentinelX
{
    public sealed class VoiceModelManager : IDisposable
    {
        // =========================================================
        // SENTINEL X.77
        // VOICE MODEL MANAGER 2.0
        // =========================================================


        // =========================================================
        // QWEN
        // =========================================================

        private const string QwenFolderName =
            "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25";


        private const string QwenArchiveName =
            "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2";


        // Approximate main model size after extraction:
        // 42 MB + 174 MB + 721 MB + tokenizer/etc.
        private const long ApproximateQwenExtractedBytes =
            950L *
            1024L *
            1024L;


        // =========================================================
        // HTTP
        // =========================================================

        private readonly HttpClient httpClient;
        private readonly Func<bool> externalNetworkAllowed;


        // =========================================================
        // PATHS
        // =========================================================

        private readonly string modelsDirectory;


        public string SileroModelPath { get; }


        public string WhisperModelPath { get; }


        public string QwenModelDirectory { get; }


        public string QwenConvFrontendPath =>
            Path.Combine(
                QwenModelDirectory,
                "conv_frontend.onnx");


        public string QwenEncoderPath =>
            Path.Combine(
                QwenModelDirectory,
                "encoder.int8.onnx");


        public string QwenDecoderPath =>
            Path.Combine(
                QwenModelDirectory,
                "decoder.int8.onnx");


        public string QwenTokenizerPath =>
            Path.Combine(
                QwenModelDirectory,
                "tokenizer");


        // =========================================================
        // CONSTRUCTOR
        // =========================================================

        public VoiceModelManager(Func<bool>? externalNetworkAllowed = null)
        {
            this.externalNetworkAllowed = externalNetworkAllowed ?? (() => true);
            modelsDirectory =
                Path.Combine(AppPaths.Root,
                    "Models");


            Directory.CreateDirectory(
                modelsDirectory);


            SileroModelPath =
                Path.Combine(
                    modelsDirectory,
                    "silero_vad.onnx");


            WhisperModelPath =
                Path.Combine(
                    modelsDirectory,
                    "ggml-small.bin");


            QwenModelDirectory =
                Path.Combine(
                    modelsDirectory,
                    QwenFolderName);

            // A portable release may include vetted models alongside the EXE.
            // An explicit data directory isolates tests and separate installations.
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENTINEL_DATA_DIR")) || Environment.GetEnvironmentVariable("SENTINEL_USE_BUNDLED_MODELS") == "1")
            {
                string bundled = Path.Combine(AppContext.BaseDirectory, "Models");
                if (File.Exists(Path.Combine(bundled, "ggml-small.bin"))) WhisperModelPath = Path.Combine(bundled, "ggml-small.bin");
                if (File.Exists(Path.Combine(bundled, "silero_vad.onnx"))) SileroModelPath = Path.Combine(bundled, "silero_vad.onnx");
            }


            httpClient =
                new HttpClient
                {
                    Timeout =
                        TimeSpan.FromMinutes(30)
                };


            httpClient
                .DefaultRequestHeaders
                .UserAgent
                .ParseAdd(
                    "SentinelX/Next");
        }


        // =========================================================
        // SILERO
        // =========================================================

        public async Task EnsureSileroAsync(
            Action<string>? status = null,
            CancellationToken cancellationToken = default)
        {
            if (IsFileValid(
                    SileroModelPath,
                    100_000))
            {
                status?.Invoke(
                    "Silero VAD • READY");


                return;
            }


            ThrowIfExternalNetworkBlocked();

            status?.Invoke(
                "Pobieranie Silero VAD...");


            Uri url =
                GetSherpaUrl(
                    "silero_vad.onnx");


            await DownloadFileAsync(
                url,
                SileroModelPath,
                status,
                cancellationToken);


            if (!IsFileValid(
                    SileroModelPath,
                    100_000))
            {
                throw new InvalidOperationException(
                    "Silero VAD został pobrany niepoprawnie.");
            }


            status?.Invoke(
                "Silero VAD • READY");
        }


        // =========================================================
        // WHISPER FALLBACK
        // =========================================================

        public bool IsSileroReady() => IsFileValid(SileroModelPath, 100_000);

        public bool IsWhisperReady()
        {
            if (!IsFileValid(WhisperModelPath, 400_000_000)) return false;
            try
            {
                using var file = File.OpenRead(WhisperModelPath);
                using var reader = new BinaryReader(file);
                return reader.ReadUInt32() == 0x67676d6c; // whisper.cpp GGML magic
            }
            catch (IOException) { return false; }
        }

        public async Task EnsureWhisperAsync(Action<string>? status = null, CancellationToken cancellationToken = default)
        {
            if (IsWhisperReady()) { status?.Invoke("Whisper Small • gotowy lokalnie"); return; }
            ThrowIfExternalNetworkBlocked();
            status?.Invoke("Pobieranie Whisper Small (około 488 MB), rozpoznawanie lokalne...");
            await DownloadFileAsync(new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"),
                WhisperModelPath, status, cancellationToken);
            if (!IsWhisperReady()) throw new InvalidDataException("Model Whisper jest niekompletny lub ma nieprawidłowy nagłówek. Spróbuj pobrać go ponownie.");
            status?.Invoke("Whisper Small • gotowy lokalnie");
        }

        public async Task EnsureQwenAsync(
            Action<string>? status = null,
            CancellationToken cancellationToken = default)
        {
            // -----------------------------------------------------
            // ALREADY INSTALLED
            // -----------------------------------------------------

            if (IsQwenReady())
            {
                status?.Invoke(
                    "Qwen3-ASR • READY");


                return;
            }


            string archivePath =
                Path.Combine(
                    modelsDirectory,
                    QwenArchiveName);


            // -----------------------------------------------------
            // DOWNLOAD
            // -----------------------------------------------------

            if (!IsFileValid(
                    archivePath,
                    10_000_000))
            {
                ThrowIfExternalNetworkBlocked();
                TryDeleteFile(
                    archivePath);


                status?.Invoke(
                    "Qwen3-ASR • pobieranie...");


                await DownloadFileAsync(
                    GetSherpaUrl(
                        QwenArchiveName),
                    archivePath,
                    status,
                    cancellationToken);
            }


            cancellationToken
                .ThrowIfCancellationRequested();


            status?.Invoke(
                "Qwen3-ASR • pobrano • sprawdzam archiwum...");


            if (!IsFileValid(
                    archivePath,
                    10_000_000))
            {
                throw new InvalidOperationException(
                    "Archiwum Qwen3-ASR jest uszkodzone albo niepełne.");
            }


            // -----------------------------------------------------
            // REMOVE BROKEN/PARTIAL OLD EXTRACTION
            // -----------------------------------------------------

            if (Directory.Exists(
                    QwenModelDirectory) &&
                !IsQwenReady())
            {
                status?.Invoke(
                    "Qwen3-ASR • czyszczenie niedokończonej instalacji...");


                TryDeleteDirectory(
                    QwenModelDirectory);
            }


            // -----------------------------------------------------
            // EXTRACT
            // -----------------------------------------------------

            await ExtractWithWindowsTarAsync(
                archivePath,
                status,
                cancellationToken);


            // -----------------------------------------------------
            // VERIFY
            // -----------------------------------------------------

            status?.Invoke(
                "Qwen3-ASR • weryfikacja modelu...");


            if (!IsQwenReady())
            {
                throw new InvalidOperationException(
                    """
                    Rozpakowywanie zakończyło się, ale model Qwen3-ASR nie przeszedł weryfikacji.

                    Brakuje co najmniej jednego z plików:
                    conv_frontend.onnx
                    encoder.int8.onnx
                    decoder.int8.onnx
                    tokenizer
                    """);
            }


            // -----------------------------------------------------
            // VERIFY SIZES
            // -----------------------------------------------------

            long frontendSize =
                new FileInfo(
                    QwenConvFrontendPath)
                    .Length;


            long encoderSize =
                new FileInfo(
                    QwenEncoderPath)
                    .Length;


            long decoderSize =
                new FileInfo(
                    QwenDecoderPath)
                    .Length;


            if (frontendSize <
                    20L * 1024L * 1024L ||
                encoderSize <
                    100L * 1024L * 1024L ||
                decoderSize <
                    500L * 1024L * 1024L)
            {
                throw new InvalidOperationException(
                    "Pliki Qwen3-ASR istnieją, ale ich rozmiary wyglądają niepoprawnie.");
            }


            // -----------------------------------------------------
            // DELETE ARCHIVE ONLY AFTER VERIFIED INSTALLATION
            // -----------------------------------------------------

            TryDeleteFile(
                archivePath);


            status?.Invoke(
                "Qwen3-ASR • READY");
        }


        // =========================================================
        // WINDOWS TAR
        // =========================================================

        private async Task ExtractWithWindowsTarAsync(
            string archivePath,
            Action<string>? status,
            CancellationToken cancellationToken)
        {
            string tarPath =
                GetTarPath();


            status?.Invoke(
                "Qwen3-ASR • rozpoczynam rozpakowywanie...");


            ProcessStartInfo startInfo =
                new ProcessStartInfo
                {
                    FileName =
                        tarPath,

                    UseShellExecute =
                        false,

                    CreateNoWindow =
                        true,

                    RedirectStandardOutput =
                        true,

                    RedirectStandardError =
                        true,

                    WorkingDirectory =
                        modelsDirectory
                };


            // tar -xjf archive.tar.bz2 -C folder
            startInfo.ArgumentList.Add(
                "-xjf");


            startInfo.ArgumentList.Add(
                archivePath);


            startInfo.ArgumentList.Add(
                "-C");


            startInfo.ArgumentList.Add(
                modelsDirectory);


            using Process process =
                new Process
                {
                    StartInfo =
                        startInfo,

                    EnableRaisingEvents =
                        true
                };


            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "Nie udało się uruchomić Windows tar.exe.");
                }


                Task<string> errorTask =
                    process
                        .StandardError
                        .ReadToEndAsync();


                Task<string> outputTask =
                    process
                        .StandardOutput
                        .ReadToEndAsync();


                int lastPercent =
                    -1;


                long lastExtractedMb =
                    -1;


                DateTime lastProgress =
                    DateTime.UtcNow;


                while (!process.HasExited)
                {
                    cancellationToken
                        .ThrowIfCancellationRequested();


                    long extracted =
                        GetCurrentQwenExtractedSize();


                    long extractedMb =
                        extracted /
                        1024 /
                        1024;


                    int percent =
                        (int)Math.Clamp(
                            extracted *
                            100 /
                            Math.Max(
                                1,
                                ApproximateQwenExtractedBytes),
                            0,
                            99);


                    if (percent !=
                            lastPercent ||
                        extractedMb !=
                            lastExtractedMb)
                    {
                        lastPercent =
                            percent;


                        lastExtractedMb =
                            extractedMb;


                        lastProgress =
                            DateTime.UtcNow;


                        status?.Invoke(
                            $"Qwen3-ASR • rozpakowywanie {percent}% • {extractedMb} MB");
                    }
                    else
                    {
                        TimeSpan silentTime =
                            DateTime.UtcNow -
                            lastProgress;


                        if (silentTime >
                            TimeSpan.FromSeconds(
                                8))
                        {
                            status?.Invoke(
                                $"Qwen3-ASR • nadal rozpakowuję • {extractedMb} MB");


                            lastProgress =
                                DateTime.UtcNow;
                        }
                    }


                    await Task.Delay(
                        500,
                        cancellationToken);
                }


                await process.WaitForExitAsync(
                    cancellationToken);


                string error =
                    await errorTask;


                _ =
                    await outputTask;


                if (process.ExitCode !=
                    0)
                {
                    throw new InvalidOperationException(
                        $"tar.exe zakończył się kodem {process.ExitCode}: {error}");
                }


                status?.Invoke(
                    "Qwen3-ASR • rozpakowywanie 100%");
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(
                    process);


                throw;
            }
            catch
            {
                TryKillProcess(
                    process);


                throw;
            }
        }


        // =========================================================
        // CURRENT EXTRACTION SIZE
        // =========================================================

        private long GetCurrentQwenExtractedSize()
        {
            if (!Directory.Exists(
                    QwenModelDirectory))
            {
                return 0;
            }


            long result =
                0;


            try
            {
                string[] importantFiles =
                {
                    QwenConvFrontendPath,
                    QwenEncoderPath,
                    QwenDecoderPath
                };


                foreach (string file
                         in importantFiles)
                {
                    if (!File.Exists(
                            file))
                    {
                        continue;
                    }


                    try
                    {
                        result +=
                            new FileInfo(
                                file)
                                .Length;
                    }
                    catch
                    {
                    }
                }


                if (Directory.Exists(
                        QwenTokenizerPath))
                {
                    foreach (
                        string file
                        in Directory.EnumerateFiles(
                            QwenTokenizerPath,
                            "*",
                            SearchOption.AllDirectories))
                    {
                        try
                        {
                            result +=
                                new FileInfo(
                                    file)
                                    .Length;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }


            return result;
        }


        // =========================================================
        // TAR LOCATION
        // =========================================================

        private static string GetTarPath()
        {
            string windowsDirectory =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Windows);


            string systemTar =
                Path.Combine(
                    windowsDirectory,
                    "System32",
                    "tar.exe");


            if (File.Exists(
                    systemTar))
            {
                return systemTar;
            }


            // Windows PATH fallback.
            return "tar.exe";
        }


        // =========================================================
        // QWEN READY
        // =========================================================

        public bool IsQwenReady()
        {
            return IsFileValid(QwenConvFrontendPath, 20L * 1024 * 1024)
                && IsFileValid(QwenEncoderPath, 100L * 1024 * 1024)
                && IsFileValid(QwenDecoderPath, 500L * 1024 * 1024)
                && IsFileValid(Path.Combine(QwenTokenizerPath, "vocab.json"), 1000)
                && IsFileValid(Path.Combine(QwenTokenizerPath, "merges.txt"), 1000);
        }

        private void ThrowIfExternalNetworkBlocked()
        {
            if (!externalNetworkAllowed())
                throw new InvalidOperationException("Tryb tylko lokalnie zablokował pobieranie modelu. Wyłącz blokadę w Settings → Pamięć tylko wtedy, gdy akceptujesz pobranie z Internetu.");
        }

        private async Task DownloadFileAsync(
            Uri url,
            string destination,
            Action<string>? status,
            CancellationToken cancellationToken)
        {
            if (!externalNetworkAllowed())
                throw new InvalidOperationException("Tryb tylko lokalnie zablokował pobieranie modelu. Wyłącz blokadę w Settings → Pamięć tylko wtedy, gdy akceptujesz pobranie z Internetu.");
            string temporary =
                destination +
                ".download";


            TryDeleteFile(
                temporary);


            try
            {
                using HttpResponseMessage response =
                    await httpClient.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);


                response
                    .EnsureSuccessStatusCode();


                long? expectedBytes =
                    response
                        .Content
                        .Headers
                        .ContentLength;


                await using Stream source =
                    await response
                        .Content
                        .ReadAsStreamAsync(
                            cancellationToken);


                await using FileStream destinationStream =
                    new FileStream(
                        temporary,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        FileOptions.Asynchronous |
                        FileOptions.SequentialScan);


                byte[] buffer =
                    new byte[
                        1024 * 1024];


                long received =
                    0;


                int lastPercent =
                    -1;


                while (true)
                {
                    int read =
                        await source.ReadAsync(
                            buffer.AsMemory(
                                0,
                                buffer.Length),
                            cancellationToken);


                    if (read ==
                        0)
                    {
                        break;
                    }


                    await destinationStream.WriteAsync(
                        buffer.AsMemory(
                            0,
                            read),
                        cancellationToken);


                    received +=
                        read;


                    if (expectedBytes.HasValue &&
                        expectedBytes.Value >
                        0)
                    {
                        int percent =
                            (int)Math.Clamp(
                                received *
                                100 /
                                expectedBytes.Value,
                                0,
                                100);


                        if (percent !=
                            lastPercent)
                        {
                            lastPercent =
                                percent;


                            status?.Invoke(
                                $"Pobieranie modelu: {percent}%");
                        }
                    }
                    else
                    {
                        long mb =
                            received /
                            1024 /
                            1024;


                        status?.Invoke(
                            $"Pobieranie modelu: {mb} MB");
                    }
                }


                await destinationStream.FlushAsync(
                    cancellationToken);


                // -------------------------------------------------
                // VERIFY CONTENT LENGTH
                // -------------------------------------------------

                if (expectedBytes.HasValue &&
                    expectedBytes.Value >
                        0 &&
                    received !=
                        expectedBytes.Value)
                {
                    throw new IOException(
                        $"Niepełne pobieranie. Oczekiwano {expectedBytes.Value} B, pobrano {received} B.");
                }


                status?.Invoke(
                    "Pobieranie modelu zakończone • zapisuję plik...");


                destinationStream.Close();


                if (File.Exists(
                        destination))
                {
                    File.Delete(
                        destination);
                }


                File.Move(
                    temporary,
                    destination);


                status?.Invoke(
                    "Model zapisany na dysku.");
            }
            catch
            {
                TryDeleteFile(
                    temporary);


                throw;
            }
        }


        // =========================================================
        // FILE VALIDATION
        // =========================================================

        private static bool IsFileValid(
            string path,
            long minimumBytes)
        {
            try
            {
                if (!File.Exists(
                        path))
                {
                    return false;
                }


                return
                    new FileInfo(
                        path)
                        .Length >=
                    minimumBytes;
            }
            catch
            {
                return false;
            }
        }


        // =========================================================
        // URL
        // =========================================================

        private static Uri GetSherpaUrl(
            string fileName)
        {
            return new Uri(
                "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" +
                fileName);
        }


        // =========================================================
        // DELETE FILE
        // =========================================================

        private static void TryDeleteFile(
            string path)
        {
            try
            {
                if (File.Exists(
                        path))
                {
                    File.Delete(
                        path);
                }
            }
            catch
            {
            }
        }


        // =========================================================
        // DELETE DIRECTORY
        // =========================================================

        private static void TryDeleteDirectory(
            string path)
        {
            try
            {
                if (Directory.Exists(
                        path))
                {
                    Directory.Delete(
                        path,
                        recursive: true);
                }
            }
            catch
            {
            }
        }


        // =========================================================
        // KILL TAR
        // =========================================================

        private static void TryKillProcess(
            Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(
                        entireProcessTree: true);
                }
            }
            catch
            {
            }
        }


        // =========================================================
        // DISPOSE
        // =========================================================

        public void Dispose()
        {
            try
            {
                httpClient.Dispose();
            }
            catch
            {
            }
        }
    }
}
