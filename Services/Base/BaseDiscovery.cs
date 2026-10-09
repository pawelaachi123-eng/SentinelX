using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace SentinelX.Services.Base;
public sealed record DiscoveredBase(string Host,int Port,ushort BaseId,string Fingerprint,string Name);
public static class BaseDiscovery
{
    public static async Task<IReadOnlyList<DiscoveredBase>> DiscoverAsync(CancellationToken cancel)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var udp=new UdpClient(AddressFamily.InterNetwork){EnableBroadcast=true};
        string nonce=Guid.NewGuid().ToString("D");
        await udp.SendAsync(Encoding.UTF8.GetBytes("SX4_DISCOVER_V421:"+nonce),new IPEndPoint(IPAddress.Broadcast,42421),timeout.Token);
        var found=new List<DiscoveredBase>();
        try{for(int received=0;received<128&&found.Count<32;received++)
        {
            var reply=await udp.ReceiveAsync(timeout.Token);
            if(reply.Buffer.Length>2048||!Private(reply.RemoteEndPoint.Address))continue;
            try{using var doc=JsonDocument.Parse(reply.Buffer,new(){MaxDepth=8});var e=doc.RootElement;
            if(e.GetProperty("protocol").GetString()!="SX4"||e.GetProperty("nonce").GetString()!=nonce)continue;
            ushort id=e.GetProperty("baseId").GetUInt16();int port=e.GetProperty("port").GetInt32();string fp=e.GetProperty("fingerprint").GetString()??"";
            if(id==0||port is <1 or >65535||fp.Length!=64||!fp.All(Uri.IsHexDigit))continue;
            var item=new DiscoveredBase(reply.RemoteEndPoint.Address.ToString(),port,id,fp.ToLowerInvariant(),SafeName(e.GetProperty("name").GetString()));
            if(!found.Any(x=>x.Host==item.Host&&x.Port==item.Port))found.Add(item);
            }catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){}
        }}
        catch(OperationCanceledException)when(!cancel.IsCancellationRequested){}
        catch(JsonException){}
        catch(KeyNotFoundException){}
        return found;
    }
    private static string SafeName(string? name)=>new((name??"Base").Where(c=>!char.IsControl(c)).Take(80).ToArray());
    private static bool Private(IPAddress address){byte[] a=address.GetAddressBytes();return a.Length==4&&(a[0]==10||a[0]==192&&a[1]==168||a[0]==172&&a[1]>=16&&a[1]<=31);}
}
