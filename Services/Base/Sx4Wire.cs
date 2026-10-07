using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace SentinelX.Services.Base;

// The binary header is the 4.2.1 contract: !HBIQH, 32-byte MAC, then raw payload.
public sealed record Sx4Frame(ushort DeviceId, byte OpCode, uint Nonce, ulong TimestampMs, byte[] Payload);
public sealed class Sx4Exception(string code) : IOException(code) { public string Code { get; } = code; }
public sealed record BaseMessage
{
    public int Version { get; init; } = 4;
    public string Type { get; init; } = "";
    public string RequestId { get; init; } = Guid.NewGuid().ToString("D");
    public string CorrelationId { get; init; } = "";
    public long ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds();
    public JsonElement Data { get; init; } = JsonSerializer.SerializeToElement(new {});
}
public static class Sx4Wire
{
    public const int HeaderSize = 49, MaxPayload = 16384;
    public static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };
    public static byte[] Encode(Sx4Frame frame, ReadOnlySpan<byte> key)
    {
        if (key.Length != 32 || frame.DeviceId == 0 || frame.Payload.Length > MaxPayload) throw new Sx4Exception("format");
        byte[] raw = new byte[HeaderSize + frame.Payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(raw, frame.DeviceId); raw[2] = frame.OpCode;
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(3), frame.Nonce);
        BinaryPrimitives.WriteUInt64BigEndian(raw.AsSpan(7), frame.TimestampMs);
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(15), (ushort)frame.Payload.Length);
        frame.Payload.CopyTo(raw.AsSpan(HeaderSize));
        byte[] input = new byte[17 + frame.Payload.Length];
        raw.AsSpan(0,17).CopyTo(input); frame.Payload.CopyTo(input.AsSpan(17));
        HMACSHA256.HashData(key, input).CopyTo(raw.AsSpan(17)); return raw;
    }
    public static Sx4Frame Decode(byte[] raw, ReadOnlySpan<byte> key)
    {
        if (key.Length != 32 || raw.Length < HeaderSize) throw new Sx4Exception("format");
        int length = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(15));
        if (length > MaxPayload || raw.Length != HeaderSize + length) throw new Sx4Exception("size");
        byte[] input = new byte[17 + length]; raw.AsSpan(0,17).CopyTo(input); raw.AsSpan(HeaderSize).CopyTo(input.AsSpan(17));
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key,input),raw.AsSpan(17,32))) throw new Sx4Exception("auth");
        ushort id = BinaryPrimitives.ReadUInt16BigEndian(raw);
        if (id == 0) throw new Sx4Exception("device");
        return new(id,raw[2],BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(3)),BinaryPrimitives.ReadUInt64BigEndian(raw.AsSpan(7)),raw[HeaderSize..]);
    }
    public static byte[] Payload(BaseMessage message)
    { Validate(message); return JsonSerializer.SerializeToUtf8Bytes(message, Json); }
    public static BaseMessage Parse(byte[] payload)
    {
        if (payload.Length > MaxPayload) throw new Sx4Exception("size");
        try
        {
            _ = new UTF8Encoding(false,true).GetString(payload);
            using var document = JsonDocument.Parse(payload,new(){ MaxDepth=16 });
            CheckDuplicateKeys(document.RootElement);
            var result = JsonSerializer.Deserialize<BaseMessage>(payload,Json) ?? throw new Sx4Exception("json");
            foreach(string required in new[]{"version","type","requestId","correlationId","expiresAt","data"})
                if (!document.RootElement.TryGetProperty(required,out _)) throw new Sx4Exception("missing_field");
            Validate(result); return result;
        }
        catch (JsonException) { throw new Sx4Exception("json"); }
        catch (DecoderFallbackException) { throw new Sx4Exception("utf8"); }
    }
    public static void Validate(BaseMessage m)
    {
        if (m.Version != 4) throw new Sx4Exception("version");
        if (!Guid.TryParseExact(m.RequestId,"D",out _) ||
            (m.CorrelationId.Length > 0 && !Guid.TryParseExact(m.CorrelationId,"D",out _))) throw new Sx4Exception("request_id");
        if (m.Type.Length is < 1 or > 64 || m.Type.Any(c=> !(char.IsAsciiLetterOrDigit(c)||c is '.' or '_'))) throw new Sx4Exception("type");
        if (m.Data.ValueKind != JsonValueKind.Object) throw new Sx4Exception("payload");
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (m.ExpiresAt <= now || m.ExpiresAt > now+300000) throw new Sx4Exception("expiry");
    }
    private static void CheckDuplicateKeys(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        { HashSet<string> keys=new(StringComparer.OrdinalIgnoreCase); foreach(var p in e.EnumerateObject()){ if(!keys.Add(p.Name))throw new Sx4Exception("duplicate_key");CheckDuplicateKeys(p.Value); } }
        else if(e.ValueKind==JsonValueKind.Array)foreach(var v in e.EnumerateArray())CheckDuplicateKeys(v);
    }
    public static async Task<byte[]> ReadAsync(Stream stream,CancellationToken cancel)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);deadline.CancelAfter(TimeSpan.FromSeconds(20));
        byte[] header=new byte[HeaderSize];await stream.ReadExactlyAsync(header,deadline.Token);
        int length=BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(15));
        if(length>MaxPayload)throw new Sx4Exception("size");
        byte[] raw=new byte[HeaderSize+length];header.CopyTo(raw,0);
        await stream.ReadExactlyAsync(raw.AsMemory(HeaderSize,length),deadline.Token);return raw;
    }
}
public sealed class Sx4Authority : IDisposable
{
    private readonly object gate=new(); private readonly ushort id; private byte[] key;
    private readonly Dictionary<uint,long> nonces=[]; private readonly Queue<long> requests=[];
    private bool revoked;
    public Sx4Authority(ushort id,byte[] key){if(id==0||key.Length!=32)throw new Sx4Exception("device");this.id=id;this.key=key.ToArray();}
    public Sx4Frame Verify(byte[] raw,long? time=null)
    {
        lock(gate)
        {
            if(revoked)throw new Sx4Exception("revoked");
            var f=Sx4Wire.Decode(raw,key);long now=time??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(f.DeviceId!=id)throw new Sx4Exception("device");
            if(f.TimestampMs>long.MaxValue||Math.Abs(now-(long)f.TimestampMs)>5000)throw new Sx4Exception("timestamp");
            foreach(var n in nonces.Where(n=>n.Value<now-10000).Select(n=>n.Key).ToArray())nonces.Remove(n);
            if(nonces.ContainsKey(f.Nonce))throw new Sx4Exception("replay");
            while(requests.Count>0&&requests.Peek()<now-60000)requests.Dequeue();
            if(requests.Count>=120||nonces.Count>=2048)throw new Sx4Exception("rate_limit");
            nonces.Add(f.Nonce,now);requests.Enqueue(now);return f;
        }
    }
    public void Dispose(){lock(gate){revoked=true;CryptographicOperations.ZeroMemory(key);nonces.Clear();requests.Clear();}}
}
