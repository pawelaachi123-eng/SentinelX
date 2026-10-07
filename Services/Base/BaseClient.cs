using System.IO;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
namespace SentinelX.Services.Base;
public sealed record BaseIdentity(string Host,int Port,string Fingerprint,ushort DeviceId,ushort BaseId,string SecretHex,string Mode="lan",string[]? Grants=null)
{
    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(Host)||Host.Length>253||Host.Any(c=>char.IsWhiteSpace(c)||c is '/' or '\\')||Port is <1 or >65535||
           Fingerprint.Length!=64||SecretHex.Length!=64||!Fingerprint.All(Uri.IsHexDigit)||!SecretHex.All(Uri.IsHexDigit)||
           DeviceId==0||BaseId==0||DeviceId==BaseId||Mode is not("lan" or "vpn" or "relay"))throw new Sx4Exception("configuration");
        if((Grants??[]).Length>32)throw new Sx4Exception("grants");
    }
}
public sealed class BaseClient : IAsyncDisposable
{
    public const byte ExtensionOpCode=201;
    private readonly BaseIdentity identity;private readonly byte[] secret; private readonly Sx4Authority authority;
    private readonly SemaphoreSlim requestGate=new(1);private readonly CancellationTokenSource stop=new();
    private TcpClient? tcp;private SslStream? stream;private uint nonce;private bool disposed;
    public HashSet<string> Capabilities{get;}=new(StringComparer.Ordinal);
    public BaseClient(BaseIdentity identity)
    {identity.Validate();this.identity=identity;secret=Convert.FromHexString(identity.SecretHex);authority=new(identity.BaseId,secret);nonce=BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));}
    public bool Supports(string type)=>Capabilities.Contains(type);
    public async Task ConnectAsync(CancellationToken cancel)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel,stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(10));
        if(disposed)throw new ObjectDisposedException(nameof(BaseClient));
        tcp=new TcpClient(); await tcp.ConnectAsync(identity.Host,identity.Port,deadline.Token);
        stream=new SslStream(tcp.GetStream(),false,(_,cert,_,_)=>cert!=null&&
            DateTime.UtcNow>=cert.GetEffectiveDateStringAsUtc() && DateTime.UtcNow<=cert.GetExpirationDateStringAsUtc() &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()),Convert.FromHexString(identity.Fingerprint)));
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost=identity.Host,EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13},deadline.Token);
        string challenge=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var proof=await ExchangeAsync("session.prove",new{challenge,role="agent",deviceId=identity.DeviceId},"session.proved",cancel);
        if(!proof.Data.TryGetProperty("challenge",out var echo)||echo.GetString()!=challenge)throw new Sx4Exception("challenge");
        var capabilities=await ExchangeAsync("capabilities",new{},"capabilities.result",cancel);
        var caps=capabilities.Data.GetProperty("types");
        if(caps.ValueKind!=JsonValueKind.Array||caps.GetArrayLength()>32)throw new Sx4Exception("capabilities");
        foreach(var c in caps.EnumerateArray())
        { string type=c.GetString()??"";if(type.Length is <1 or >64||!Capabilities.Add(type))throw new Sx4Exception("capabilities"); }
    }
    public Task<BaseMessage> RequestAsync(string type,object data,CancellationToken cancel)
    {if(!Supports(type))throw new Sx4Exception("unsupported");return ExchangeAsync(type,data,type+".result",cancel);}
    private async Task<BaseMessage> ExchangeAsync(string type,object data,string expected,CancellationToken cancel)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel,stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(20));
        await requestGate.WaitAsync(deadline.Token);
        try
        {
            if(disposed||stream==null)throw new Sx4Exception("offline");
            var message=new BaseMessage{Type=type,Data=JsonSerializer.SerializeToElement(data)};
            byte[] raw=Sx4Wire.Encode(new(identity.DeviceId,ExtensionOpCode,unchecked(++nonce),(ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Sx4Wire.Payload(message)),secret);
            await stream.WriteAsync(raw,deadline.Token);await stream.FlushAsync(deadline.Token);
            var frame=authority.Verify(await Sx4Wire.ReadAsync(stream,deadline.Token));
            if(frame.OpCode!=ExtensionOpCode)throw new Sx4Exception("opcode");
            var response=Sx4Wire.Parse(frame.Payload);
            if(response.CorrelationId!=message.RequestId)throw new Sx4Exception("correlation");
            if(response.Type=="error"){string code=response.Data.GetProperty("code").GetString()??"remote_error";throw new Sx4Exception(code);}
            if(response.Type!=expected)throw new Sx4Exception("response_type");
            return response;
        }
        catch {stream?.Dispose();tcp?.Dispose();throw;}
        finally{requestGate.Release();}
    }
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;await stop.CancelAsync();tcp?.Dispose();stream?.Dispose();
        await requestGate.WaitAsync();try{authority.Dispose();CryptographicOperations.ZeroMemory(secret);}finally{requestGate.Release();}
    }
}

internal static class CertificateDates
{
 public static DateTime GetEffectiveDateStringAsUtc(this System.Security.Cryptography.X509Certificates.X509Certificate cert)=>new System.Security.Cryptography.X509Certificates.X509Certificate2(cert).NotBefore.ToUniversalTime();
 public static DateTime GetExpirationDateStringAsUtc(this System.Security.Cryptography.X509Certificates.X509Certificate cert)=>new System.Security.Cryptography.X509Certificates.X509Certificate2(cert).NotAfter.ToUniversalTime();
}
