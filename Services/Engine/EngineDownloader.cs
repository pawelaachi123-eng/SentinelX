using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace SentinelX.Services.Engine;

/// <summary>Resumable, verified downloads: bytes go to "*.part", the transfer is retried and resumed with HTTP Range,
/// and the file gets its final name only after size and SHA-256 match the pinned values.</summary>
public sealed class EngineDownloader : IDisposable
{
    private readonly HttpClient http;
    private readonly int maxAttempts;
    private readonly TimeSpan retryStep;

    public EngineDownloader(HttpMessageHandler? handler = null, int maxAttempts = 6, TimeSpan? retryStep = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 8 })
        { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelX-Engine/0.96");
        this.maxAttempts = Math.Max(1, maxAttempts);
        this.retryStep = retryStep ?? TimeSpan.FromSeconds(5);
    }

    public async Task DownloadAsync(string url, string destination, long expectedSize, string expectedSha256, IProgress<long>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string part = destination + ".part";
        for (int attempt = 1; ; attempt++)
        {
            try { await DownloadOnceAsync(url, part, expectedSize, progress, token).ConfigureAwait(false); break; }
            catch (Exception ex) when (attempt < maxAttempts && (ex is HttpRequestException or IOException or TimeoutException))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, retryStep.TotalSeconds * attempt)), token).ConfigureAwait(false);
            }
        }
        string actual = await HashFileAsync(part, token).ConfigureAwait(false);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(part);
            throw new InvalidDataException("Suma kontrolna pobranego pliku nie zgadza się z wzorcem — plik usunięto, zostanie pobrany od nowa.");
        }
        File.Move(part, destination, true);
    }

    private async Task DownloadOnceAsync(string url, string part, long expectedSize, IProgress<long>? progress, CancellationToken token)
    {
        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing > expectedSize) { TryDelete(part); existing = 0; }
        if (existing == expectedSize) { progress?.Report(existing); return; }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { TryDelete(part); throw new IOException("Serwer odrzucił wznowienie pobierania — zaczynam od początku."); }
        response.EnsureSuccessStatusCode();
        if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent) existing = 0; // the server ignored Range

        await using Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        byte[] buffer = new byte[1 << 16];
        long done = existing;
        progress?.Report(done);
        while (true)
        {
            int read;
            using (var stall = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                stall.CancelAfter(TimeSpan.FromSeconds(90)); // no bytes for 90 s = broken connection: retry and resume
                try { read = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Brak danych z serwera przez 90 s."); }
            }
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            done += read;
            progress?.Report(done);
        }
        await output.FlushAsync(token).ConfigureAwait(false);
        if (done != expectedSize) throw new IOException($"Pobrano {done} z {expectedSize} bajtów — połączenie zostało przerwane.");
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => http.Dispose();
}
