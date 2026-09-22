namespace SentinelX.Services.Files;
public interface IFileService
{
    string? LastFile { get; }
    Task<string?> ProcessAsync(string command, CancellationToken token);
}
