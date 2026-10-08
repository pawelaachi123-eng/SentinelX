using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
namespace SentinelX.Services.Base;
public sealed class OllamaSupervisor : IDisposable
{
    private readonly SemaphoreSlim gate=new(1);
    private readonly Func<CancellationToken,Task<bool>> probe;private readonly Func<bool> launch;
    private long lastLaunch=long.MinValue;
    private readonly Dictionary<string,long> modelUsed=new(StringComparer.Ordinal);
    public bool AutoLoadModel=>false;
    public OllamaSupervisor(Func<CancellationToken,Task<bool>>? probe=null,Func<bool>? launch=null)
    {this.probe=probe??ProbeAsync;this.launch=launch??Launch;}
    public async Task<bool> EnsureAsync(CancellationToken cancel)
    {
        await gate.WaitAsync(cancel);try{
            if(await probe(cancel))return true;
            long now=Environment.TickCount64;if(lastLaunch!=long.MinValue&&now-lastLaunch<30000)return false;
            lastLaunch=now;if(!launch())return false;
            for(int i=0;i<10;i++){await Task.Delay(300,cancel);if(await probe(cancel))return true;}return false;
        }finally{gate.Release();}
    }
    public Task<bool> StatusAsync(CancellationToken cancel)=>probe(cancel);
    public static bool IsModelName(string value)=>!string.IsNullOrEmpty(value)&&value.Length<=128&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or ':' or '/' or '-' or '_');
    public static string GenerateRequest(string model,string keepAlive)=>JsonSerializer.Serialize(new{model,prompt="",stream=false,keep_alive=keepAlive});
    public int TrackedModels{get{lock(modelUsed)return modelUsed.Count;}}
    public async Task<bool> EnsureModelAsync(string model,int keepAliveMinutes,CancellationToken cancel)
    {
        if(!IsModelName(model)||keepAliveMinutes is <1 or >1440)return false;
        await gate.WaitAsync(cancel);try{
            if(!await probe(cancel))return false;
            if(await GenerateAsync(model,keepAliveMinutes+"m",cancel)){lock(modelUsed)modelUsed[model]=Environment.TickCount64;return true;}
            return false;
        }finally{gate.Release();}
    }
    public async Task<int> ReleaseIdleModelsAsync(int idleMinutes,CancellationToken cancel)
    {
        if(idleMinutes<1)return 0;
        string[] idle;long now=Environment.TickCount64;
        lock(modelUsed)idle=modelUsed.Where(x=>now-x.Value>TimeSpan.FromMinutes(idleMinutes).Ticks).Select(x=>x.Key).ToArray();
        int released=0;
        foreach(var model in idle){
            cancel.ThrowIfCancellationRequested();
            if(await GenerateAsync(model,"0m",cancel)){lock(modelUsed)modelUsed.Remove(model);released++;}
        }
        return released;
    }
    private static async Task<bool> GenerateAsync(string model,string keepAlive,CancellationToken cancel)
    {
        try{
            using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(30)};
            using var content=new StringContent(GenerateRequest(model,keepAlive),System.Text.Encoding.UTF8,"application/json");
            using var response=await client.PostAsync("http://127.0.0.1:11434/api/generate",content,cancel);
            response.EnsureSuccessStatusCode();return true;
        }catch(Exception e)when(e is HttpRequestException or TaskCanceledException){return false;}
    }
    private static async Task<bool> ProbeAsync(CancellationToken cancel)
    {
        try{
            using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(2)};
            using var response=await client.GetAsync("http://127.0.0.1:11434/api/tags",HttpCompletionOption.ResponseHeadersRead,cancel);
            response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>524288)return false;
            using var input=await response.Content.ReadAsStreamAsync(cancel);using var output=new MemoryStream();byte[] b=new byte[8192];
            int n;while((n=await input.ReadAsync(b,cancel))>0){if(output.Length+n>524288)return false;output.Write(b,0,n);}
            using var json=JsonDocument.Parse(output.ToArray());return json.RootElement.GetProperty("models").ValueKind==JsonValueKind.Array;
        }catch(Exception e)when(e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException){return false;}
    }
    private static bool Launch()
    {
        string exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Ollama","ollama.exe");
        if(!File.Exists(exe))return false;
        if(Process.GetProcessesByName("ollama").Any())return false;
        Process.Start(new ProcessStartInfo(exe,"serve"){UseShellExecute=false,CreateNoWindow=true});return true;
    }
    public void Dispose()=>gate.Dispose();
}
