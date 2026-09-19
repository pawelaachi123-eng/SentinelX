using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace SentinelX;

public sealed record VoiceUtteranceRecord
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public int DeviceNumber { get; init; }
    public string DeviceName { get; init; } = "";
    public DateTimeOffset VadStart { get; init; }
    public DateTimeOffset VadEnd { get; init; }
    public VoiceSegmentMetrics? Signal { get; init; }
    public string Engine { get; init; } = "";
    public long QueueLatencyMilliseconds { get; init; }
    public long AsrLatencyMilliseconds { get; init; }
    public string? Transcript { get; init; }
    public bool TranscriptRedacted { get; init; } = true;
    public bool WakeRequired { get; init; }
    public string Decision { get; init; } = "pending";
    public string? Error { get; init; }
}

/// <summary>Bounded asynchronous metadata writer. A slow disk never blocks microphone capture.</summary>
public sealed class VoiceUtteranceLogService : IAsyncDisposable
{
    private readonly Channel<VoiceUtteranceRecord> entries = Channel.CreateBounded<VoiceUtteranceRecord>(
        new BoundedChannelOptions(128) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task writer;
    private readonly long maximumBytes;
    private readonly int retainedFiles;
    private long droppedEntries;
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public string LogPath { get; }
    public string? LastError { get; private set; }
    public long DroppedEntries => Interlocked.Read(ref droppedEntries);

    public VoiceUtteranceLogService(string? directory = null, long maximumBytes = 2 * 1024 * 1024, int retainedFiles = 4)
    {
        LogPath = Path.Combine(directory ?? Path.Combine(AppPaths.Root, "Voice"), "utterances.jsonl");
        this.maximumBytes = Math.Clamp(maximumBytes, 1024, 32 * 1024 * 1024);
        this.retainedFiles = Math.Clamp(retainedFiles, 1, 10);
        writer = Task.Run(WriteLoopAsync);
    }

    public bool TryWrite(VoiceUtteranceRecord record)
    {
        if (entries.Writer.TryWrite(record)) return true;
        Interlocked.Increment(ref droppedEntries);
        return false;
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var record in entries.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                string line = JsonSerializer.Serialize(record, Options) + "\n";
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length + Encoding.UTF8.GetByteCount(line) > maximumBytes)
                {
                    for (int index = retainedFiles - 1; index >= 1; index--)
                    {
                        string source = LogPath + "." + index;
                        if (File.Exists(source)) File.Move(source, LogPath + "." + (index + 1), true);
                    }
                    File.Move(LogPath, LogPath + ".1", true);
                }
                await File.AppendAllTextAsync(LogPath, line, Encoding.UTF8).ConfigureAwait(false);
                LastError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                LastError = ex.Message;
                Interlocked.Increment(ref droppedEntries);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        entries.Writer.TryComplete();
        await writer.ConfigureAwait(false);
    }
}
