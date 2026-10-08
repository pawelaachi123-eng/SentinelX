using System.IO;
using System.Net;
using System.Net.Http;

namespace SentinelX.Services.Maintenance;

/// <summary>HTTPS download with resume and bounded retries. There is no default
/// server in Build 1.0 (no update manifest by user decision) — the caller
/// supplies explicit https URLs, and every byte is still verified by
/// VerifiedUpdater (size, SHA-256, version, signature) before staging.</summary>
public static class UpdateDownloader
{
    public const int MaxAttempts = 4;

    public static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "https" || !string.IsNullOrEmpty(address.UserInfo))
            throw new UpdateFailure("url");
    }

    public static async Task DownloadAsync(HttpClient http, string url, string destination, long expectedSize, IProgress<double>? progress, CancellationToken cancel)
    {
        ValidateUrl(url);
        if (expectedSize is < 1024 or > 536870912) throw new UpdateFailure("metadata");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        long offset = File.Exists(destination) ? new FileInfo(destination).Length : 0;
        if (offset > expectedSize)
        {
            File.Delete(destination);
            offset = 0;
        }
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (offset > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    if (response.Content.Headers.ContentRange?.Length != expectedSize) throw new UpdateFailure("size");
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    offset = 0; // Server ignored Range: restart without resume.
                }
                using var input = await response.Content.ReadAsStreamAsync(cancel);
                using var output = new FileStream(destination, offset > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None, 65536, true);
                var buffer = new byte[65536];
                long received = offset;
                int read;
                while ((read = await input.ReadAsync(buffer, cancel)) > 0)
                {
                    received += read;
                    if (received > expectedSize) throw new UpdateFailure("size");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                    progress?.Report((double)received / expectedSize);
                }
                await output.FlushAsync(cancel);
                if (new FileInfo(destination).Length != expectedSize) throw new UpdateFailure("size");
                return;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
            {
                if (attempt == MaxAttempts) throw new UpdateFailure("download");
                offset = File.Exists(destination) ? new FileInfo(destination).Length : 0;
                if (offset > expectedSize)
                {
                    File.Delete(destination);
                    offset = 0;
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble()), cancel);
            }
        }
        throw new UpdateFailure("download");
    }

    public static async Task<string> DownloadStringAsync(HttpClient http, string url, int maxBytes, CancellationToken cancel)
    {
        ValidateUrl(url);
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > maxBytes) throw new UpdateFailure("metadata_size");
                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancel);
                if (bytes.Length > maxBytes) throw new UpdateFailure("metadata_size");
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                if (attempt == 3) throw new UpdateFailure("download");
                await Task.Delay(TimeSpan.FromSeconds(attempt + Random.Shared.NextDouble()), cancel);
            }
        }
        throw new UpdateFailure("download");
    }
}
