using System.IO;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SentinelX.Core;
using SentinelX.Services.Desktop;
using SentinelX.Services.Link;
namespace SentinelX.Services.Base;
public sealed class WindowsBaseService : IBaseControl
{
    private readonly IDesktopService desktop;private readonly SystemMonitor monitor;private readonly OllamaSupervisor ollama;private readonly AlertFeed alerts;
    private readonly string folder=Path.Combine(AppPaths.Root,"Base");private readonly SemaphoreSlim connectGate=new(1);private readonly object stateGate=new();
    private BaseIdentity? identity;private BaseClient? client;private CancellationTokenSource? stop;private Task? loop,worker;
    private readonly AgentQueue queue;private string status="Base nie jest sparowana";private bool authBlocked,disposed;
    public event Action? Changed;
    public string Status{get{lock(stateGate)return status;}}
    /// <summary>Set by the headless Agent: queue consent is denied (nobody can confirm),
    /// and window-bound operations become no-ops instead of touching the UI thread.</summary>
    public bool Headless{get;set;}
    public BaseIdentity? PublicConfiguration=>identity is{} i?i with{SecretHex=""}:null;
    public IReadOnlyList<string> Capabilities=>client?.Capabilities.ToArray()??[];
    private static byte[] Protect(byte[] value)=>ProtectedData.Protect(value,null,DataProtectionScope.CurrentUser);
    private static byte[] Unprotect(byte[] value)=>ProtectedData.Unprotect(value,null,DataProtectionScope.CurrentUser);
    public WindowsBaseService(IDesktopService desktop,SystemMonitor monitor,OllamaSupervisor ollama,AlertFeed alerts)
    {
        this.desktop=desktop;this.monitor=monitor;this.ollama=ollama;this.alerts=alerts;
        queue=new(Path.Combine(folder,"queue.bin"),Protect,Unprotect);
        queue.StateChanged+=task=>Audit(task.Id,task.DeviceId,task.Operation,AuditTarget(task),task.State,task.Code);
        try{string p=Path.Combine(folder,"identity.bin");if(File.Exists(p)){
            if(new FileInfo(p).Length>65536)throw new IOException("size");
            identity=JsonSerializer.Deserialize<BaseIdentity>(Unprotect(File.ReadAllBytes(p)));identity?.Validate();}}
        catch(Exception e)when(e is IOException or CryptographicException or JsonException or Sx4Exception){authBlocked=true;SetStatus("Uszkodzona konfiguracja Base — sparuj ponownie");}
    }
    public void Start()
    {
        if(identity==null||authBlocked||disposed||loop is{IsCompleted:false}||Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE")=="1")return;
        stop=new();loop=Task.Run(()=>RunAsync(stop.Token));worker=Task.Run(()=>WorkerAsync(stop.Token));
    }
    public async Task PairAsync(BaseIdentity next,CancellationToken cancel)
    {
        next.Validate();
        if((next.Grants??[]).Any(x=>!AgentQueue.Operations.Contains(x)))throw new Sx4Exception("grants");
        await using(var verify=new BaseClient(next)){await verify.ConnectAsync(cancel);}
        await StopAsync();await queue.CancelAllAsync("pairing_changed");Directory.CreateDirectory(folder);string p=Path.Combine(folder,"identity.bin");
        AtomicSecret(p,JsonSerializer.SerializeToUtf8Bytes(next));identity=next;authBlocked=false;
        Audit("pair",next.BaseId,"pair","", "succeeded","");Start();
    }
    public async Task UnpairAsync()
    {
        await StopAsync();await queue.CancelAllAsync("permission_revoked");if(identity!=null)Audit("unpair",identity.BaseId,"revoke","","succeeded","");
        string p=Path.Combine(folder,"identity.bin");if(File.Exists(p))File.Delete(p);
        identity=null;authBlocked=true;SetStatus("Urządzenie odłączone. Zmień lub unieważnij jego sekret również w Base.");
    }
    public async Task StopAsync()
    {
        stop?.Cancel();var c=client;if(c!=null)await c.DisposeAsync();
        if(loop!=null)try{await loop;}catch(OperationCanceledException){}
        if(worker!=null)try{await worker;}catch(OperationCanceledException){}
        client=null;stop?.Dispose();stop=null;
    }
    public Task<IReadOnlyList<AgentTask>> TasksAsync()=>queue.SnapshotAsync();
    public async Task<BaseMessage> RequestAsync(string type,object data,CancellationToken cancel)
    {
        await connectGate.WaitAsync(cancel);try{if(client==null||authBlocked)throw new Sx4Exception("offline");return await client.RequestAsync(type,data,cancel);}
        finally{connectGate.Release();}
    }
    public async Task<string> RepairQueueAsync()
    {
        await StopAsync();Audit("repair",identity?.BaseId??0,"queue","queue","detect","");
        try{
            string backup=await queue.RepairAsync();Audit("repair",identity?.BaseId??0,"queue","queue","backup","");
            Audit("repair",identity?.BaseId??0,"queue","queue","repair","");Audit("repair",identity?.BaseId??0,"queue","queue","verify","succeeded");
            Start();return backup;
        }catch{Audit("repair",identity?.BaseId??0,"queue","queue","result","failed");Start();throw;}
    }
    private async Task RunAsync(CancellationToken cancel)
    {
        int retries=0;
        while(!cancel.IsCancellationRequested&&identity!=null&&!authBlocked)
        {
            var configured=identity;BaseClient? connection=null;
            try
            {
                SetStatus("Łączenie z Base · "+configured.Mode.ToUpperInvariant());
                connection=new(configured);await connection.ConnectAsync(cancel);client=connection;retries=0;
                if(!connection.Supports("pc.heartbeat"))throw new Sx4Exception("unsupported");
                SetStatus("Base połączona · SX4 · "+configured.Mode.ToUpperInvariant());
                while(!cancel.IsCancellationRequested)
                {
                    await RequestAsync("pc.heartbeat",Telemetry(),cancel);
                    if(connection.Supports("tasks.poll")){
                        var result=await RequestAsync("tasks.poll",new{},cancel);
                        if(result.Data.TryGetProperty("tasks",out var tasks)){
                            if(tasks.ValueKind!=JsonValueKind.Array||tasks.GetArrayLength()>32)throw new Sx4Exception("size");
                            foreach(var data in tasks.EnumerateArray()){
                                var task=data.Deserialize<AgentTask>(new JsonSerializerOptions{PropertyNameCaseInsensitive=false,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})??throw new Sx4Exception("task");
                                await queue.AddAsync(task,new HashSet<string>(configured.Grants??[]));Audit(task.Id,task.DeviceId,task.Operation,AuditTarget(task),"accepted","");
                            }
                        }
                    }
                    if(connection.Supports("tasks.status")){
                        var statuses=await queue.SnapshotAsync();
                        foreach(var batch in statuses.Chunk(32))await RequestAsync("tasks.status",new{tasks=batch.Select(x=>new{id=x.Id,state=x.State,code=x.Code})},cancel);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(15),cancel);
                }
            }
            catch(OperationCanceledException)when(cancel.IsCancellationRequested){break;}
            catch(Exception e)when(e is Sx4Exception or System.Security.Authentication.AuthenticationException or CryptographicException or JsonException or InvalidOperationException or KeyNotFoundException){
                authBlocked=true;stop?.Cancel();await queue.CancelAllAsync("authorization_blocked");SetStatus("Base: przerwano bezpiecznie ("+(e is Sx4Exception x?x.Code:"auth_or_protocol")+"). Sprawdź konfigurację i sparuj ponownie.");break;
            }
            catch(Exception e)when(e is IOException or System.Net.Sockets.SocketException or OperationCanceledException){SetStatus("Base offline — ponawiam połączenie");}
            finally{client=null;if(connection!=null)await connection.DisposeAsync();}
            if(configured.Mode=="lan")
            {
                var found=await BaseDiscovery.DiscoverAsync(cancel);
                var moved=found.FirstOrDefault(x=>x.BaseId==configured.BaseId&&x.Fingerprint.Equals(configured.Fingerprint,StringComparison.OrdinalIgnoreCase));
                if(moved!=null)identity=configured with{Host=moved.Host,Port=moved.Port};
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(60,Math.Pow(2,Math.Min(++retries,6)))+Random.Shared.NextDouble()),cancel);
        }
    }
    private async Task WorkerAsync(CancellationToken cancel)
    {
        while(!cancel.IsCancellationRequested)
        {
            var grants=new HashSet<string>(authBlocked?[]:identity?.Grants??[]);
            try{await queue.DrainAsync(IsUnlocked,ConsentAsync,ExecuteAsync,grants,cancel);}
            catch(OperationCanceledException)when(cancel.IsCancellationRequested){break;}
            catch(Exception){SetStatus("Kolejka Agenta wymaga sprawdzenia");}
            await Task.Delay(1000,cancel);
        }
    }
    private object Telemetry()
    {
        static double? Available(double x)=>double.IsFinite(x)?x:null;
        var disks=DriveInfo.GetDrives().Where(x=>x.IsReady&&x.DriveType==DriveType.Fixed).Take(16).Select(x=>new{drive=x.Name,total=x.TotalSize,free=x.AvailableFreeSpace}).ToArray();
        var network=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up&&x.NetworkInterfaceType!=NetworkInterfaceType.Loopback).Take(16).Select(x=>{
            var stats=x.GetIPStatistics();return new{mac=x.GetPhysicalAddress().ToString(),received=stats.BytesReceived,sent=stats.BytesSent};}).ToArray();
        return new{version=AppConstants.SemanticVersion,cpu=Available(monitor.GetCpuUsage()),ramUsedGb=Available(monitor.GetUsedRamGB()),ramTotalGb=Available(monitor.GetTotalRamGB()),gpu=Available(monitor.GetGpuUsagePercent()),disks,network,uptimeMs=Environment.TickCount64,sessionUnlocked=IsUnlocked(),autoLoadModel=false};
    }
    private async Task ExecuteAsync(AgentTask task,CancellationToken cancel)
    {
        if(!IsUnlocked())throw new Sx4Exception("session_locked");
        switch(task.Operation){
            case "lock":if(!LockWorkStation())throw new Sx4Exception("lock_failed");break;
            case "sentinel.show":if(!Headless)Application.Current.Dispatcher.Invoke(desktop.ShowWindow);break;
            case "notification":alerts.Add("info","Sentinel Base",task.Target);break;
            case "ollama.ensure":if(!await ollama.EnsureAsync(cancel))throw new Sx4Exception("ollama_unavailable");break;
            case "steam.run":case "steam.install":
                string steam=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam","steam.exe");
                if(!File.Exists(steam))throw new Sx4Exception("steam_not_installed");
                Process.Start(new ProcessStartInfo(steam,(task.Operation=="steam.run"?"steam://rungameid/":"steam://install/")+task.Target){UseShellExecute=false});break;
            case "restart":case "shutdown":
                using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"shutdown.exe"),task.Operation=="restart"?"/r /t 30":"/s /t 30"){UseShellExecute=false,CreateNoWindow=true})??throw new IOException("launch")){
                    await p.WaitForExitAsync(cancel);if(p.ExitCode!=0)throw new Sx4Exception("shutdown_failed");}break;
            case "sleep":if(!ShutdownPrivilege.Sleep())throw new Sx4Exception("sleep_failed");break;
            default:throw new Sx4Exception("permission");
        }
        Audit(task.Id,task.DeviceId,task.Operation,AuditTarget(task),"submitted",task.Operation.StartsWith("steam.",StringComparison.Ordinal)?"steam_interaction_required":"");
    }
    private Task<bool> ConsentAsync(AgentTask task,CancellationToken cancel)=>Headless?Task.FromResult(false):ConfirmAsync(task,cancel);
    public static Task<bool> ConfirmAsync(AgentTask task,CancellationToken cancel)
    {
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Current.Dispatcher.BeginInvoke(()=>{
            if(cancel.IsCancellationRequested){completion.TrySetResult(false);return;}
            var yes=new Button{Content="Zatwierdź lokalnie",Margin=new Thickness(8)};
            var no=new Button{Content="Anuluj",Margin=new Thickness(8)};
            var stack=new StackPanel{Margin=new Thickness(20)};
            stack.Children.Add(new TextBlock{Text="Urządzenie "+task.DeviceId+" prosi o: "+task.Operation+" "+task.Target+"\nŻądanie: "+task.Id+"\nZgoda wygaśnie automatycznie.",TextWrapping=TextWrapping.Wrap});
            stack.Children.Add(yes);stack.Children.Add(no);
            var window=new Window{Title="Sentinel X — potwierdzenie",Width=460,Height=260,Content=stack,WindowStartupLocation=WindowStartupLocation.CenterScreen};
            yes.Click+=(_,_)=>{completion.TrySetResult(!cancel.IsCancellationRequested&&IsUnlocked());window.Close();};
            no.Click+=(_,_)=>{completion.TrySetResult(false);window.Close();};
            window.Closed+=(_,_)=>completion.TrySetResult(false);
            var registration=cancel.Register(()=>Application.Current.Dispatcher.BeginInvoke(()=>{completion.TrySetResult(false);window.Close();}));
            window.Closed+=(_,_)=>registration.Dispose();window.Show();
        });
        return completion.Task;
    }
    private static bool IsUnlocked()
    {
        if(!Environment.UserInteractive)return false;
        IntPtr handle=OpenInputDesktop(0,false,1);if(handle==IntPtr.Zero)return false;
        try{var name=new System.Text.StringBuilder(256);return GetUserObjectInformation(handle,2,name,512,out _)&&name.ToString()=="Default";}
        finally{CloseDesktop(handle);}
    }
    private static void AtomicSecret(string path,byte[] data)
    {
        byte[] encrypted=Protect(data);string tmp=path+".tmp";
        try{using(var f=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(encrypted);f.Flush(true);}
            if(!Unprotect(File.ReadAllBytes(tmp)).AsSpan().SequenceEqual(data))throw new IOException("verify");File.Move(tmp,path,true);}
        finally{CryptographicOperations.ZeroMemory(data);if(File.Exists(tmp))File.Delete(tmp);}
    }
    private static string AuditTarget(AgentTask task)=>task.Operation=="notification"?"local_notification":task.Target;
    private void Audit(string id,ushort device,string action,string target,string state,string code)
    {
        Directory.CreateDirectory(folder);string p=Path.Combine(folder,"audit.jsonl");
        lock(stateGate){
            if(File.Exists(p)&&new FileInfo(p).Length>8*1024*1024)File.Move(p,p+".previous",true);
            File.AppendAllText(p,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,id,device,action,target,state,code})+"\n");}
    }
    private void SetStatus(string value){lock(stateGate)status=value;Changed?.Invoke();}
    public void Dispose(){if(disposed)return;disposed=true;stop?.Cancel();client?.DisposeAsync().AsTask().GetAwaiter().GetResult();}
    [DllImport("user32.dll",SetLastError=true)]private static extern bool LockWorkStation();
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",EntryPoint="GetUserObjectInformationW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetUserObjectInformation(IntPtr handle,int index,System.Text.StringBuilder data,uint length,out uint needed);
}
