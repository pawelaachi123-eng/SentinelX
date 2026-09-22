using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SentinelX.Services.History;

/// <summary>User-requested, local snapshot export. Never exports conversation automatically or overwrites a file.</summary>
public sealed class HistoryExportService(ActionHistoryService history, string? root = null)
{
    public async Task<ActionExecutionResult> ExportAsync(string format, CancellationToken token = default)
    {
        if (format is not ("json" or "csv")) return ActionExecutionResult.Failure("Format eksportu: json albo csv.");
        string? temporary = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var entries = history.GetRecentEntries(200);
            if ((history.LastReadError ?? history.LastStorageError) is { } error) return ActionExecutionResult.Failure("Eksport przerwany: historia nie została w pełni odczytana lub zapisana.", error);
            string content = format == "json"
                ? JsonSerializer.Serialize(new { schemaVersion = 1, exportedAt = DateTimeOffset.UtcNow,
                    scope = "Latest states of at most 200 actions; not the full journal. Contains private commands and paths.", count = entries.Count, actions = entries }, new JsonSerializerOptions { WriteIndented = true })
                : ToCsv(entries);
            byte[] body = Encoding.UTF8.GetBytes(content);
            if (body.Length > 8 * 1024 * 1024) return ActionExecutionResult.Failure("Eksport przekracza bezpieczny limit 8 MiB. Nie zapisano pliku.");
            string directory = Path.Combine(root ?? AppPaths.Root, "Exports");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"historia-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.{format}");
            temporary = path + ".partial";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            { await stream.WriteAsync(body, token); await stream.FlushAsync(token); }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, false); temporary = null;
            // Publication already happened: finish read-back even if cancellation arrives now.
            byte[] readBack = await File.ReadAllBytesAsync(path);
            if (!body.AsSpan().SequenceEqual(readBack)) return ActionExecutionResult.Failure("Plik zapisano, ale odczyt zwrotny się nie zgadza.", path);
            return ActionExecutionResult.VerifiedSuccess($"Eksport {entries.Count} stanów akcji zapisany lokalnie:\n{path}",
                $"Odczyt zwrotny zgodny; {readBack.Length} bajtów; SHA-256: {Convert.ToHexString(SHA256.HashData(readBack))}.\nPlik zawiera prywatne komendy i ścieżki. Nie jest pełnym dziennikiem ani eksportem rozmowy.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return ActionExecutionResult.Failure("Nie udało się wyeksportować historii.", ex.Message); }
        finally
        {
            if (temporary != null) try { File.Delete(temporary); } catch (Exception ex) { AppLog.Write(ex); }
        }
    }
    internal static string CsvCell(string value)
    {
        // Quoting alone does not prevent spreadsheet formula injection.
        string trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static string ToCsv(IReadOnlyList<ActionHistoryEntry> entries)
    {
        var output = new StringBuilder("ActionId,RequestId,Timestamp,Type,Status,Command,Message,Evidence,DurationMilliseconds\r\n");
        foreach (var x in entries)
            output.AppendLine(string.Join(",", new[] { x.ActionId, x.RequestId, x.Timestamp.ToString("O", CultureInfo.InvariantCulture), x.ActionType,
                x.Status, x.Command, x.Message, x.Evidence, x.DurationMilliseconds.ToString(CultureInfo.InvariantCulture) }.Select(CsvCell)));
        return output.ToString();
    }
}
