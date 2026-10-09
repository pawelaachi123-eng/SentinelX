using System.IO;
using System.Text.Json;
namespace SentinelX.Services.Base;
public sealed record AgentTask(string Id,ushort DeviceId,string Operation,string Target,long ExpiresAt,string State="accepted",string Code="");
public sealed class AgentQueue
{
    public static readonly HashSet<string> Operations=new(StringComparer.Ordinal){"lock","sleep","restart","shutdown","steam.run","steam.install","ollama.ensure","sentinel.show","notification"};
    public static bool NeedsConsent(string op)=>op is "sleep" or "restart" or "shutdown" or "steam.install";
    private readonly string path;private readonly Func<byte[],byte[]> protect,unprotect;private readonly SemaphoreSlim gate=new(1);
    public event Action<AgentTask>? StateChanged;
    private List<AgentTask> items=[];public string? StorageError{get;private set;}
    public AgentQueue(string path,Func<byte[],byte[]> protect,Func<byte[],byte[]> unprotect)
    {
        this.path=path;this.protect=protect;this.unprotect=unprotect;
        try
        {
            if(File.Exists(path)){if(new FileInfo(path).Length is <32 or >2*1024*1024)throw new IOException("size");items=JsonSerializer.Deserialize<List<AgentTask>>(unprotect(File.ReadAllBytes(path)))??[];
                if(items.Count>256||items.Select(x=>x.Id).Distinct().Count()!=items.Count)throw new IOException("queue");
                foreach(var i in items)Validate(i);
                items=items.Select(x=>x.State=="running"?x with{State="failed",Code="interrupted"}:x).ToList();Save();}
        }
        catch(Exception e)when(e is IOException or System.Security.Cryptography.CryptographicException or JsonException or Sx4Exception){StorageError="queue_corrupt";items=[];}
    }
    public static void Validate(AgentTask task)
    {
        if(task is null||task.Target is null||task.Operation is null||!Guid.TryParseExact(task.Id,"D",out _)||task.DeviceId==0||!Operations.Contains(task.Operation)||
           task.State is not("accepted" or "running" or "succeeded" or "failed" or "expired" or "cancelled"))throw new Sx4Exception("task");
        if(task.Operation.StartsWith("steam.",StringComparison.Ordinal))
        {if(!uint.TryParse(task.Target,out uint id)||id==0||id.ToString()!=task.Target)throw new Sx4Exception("steam_appid");}
        else if(task.Operation=="notification"){if(task.Target.Length>240||task.Target.Any(c=>char.IsControl(c)&&c!='\n'))throw new Sx4Exception("target");}
        else if(task.Target.Length!=0)throw new Sx4Exception("target");
    }
    public async Task<IReadOnlyList<AgentTask>> SnapshotAsync(){await gate.WaitAsync();try{return items.ToArray();}finally{gate.Release();}}
    public async Task<AgentTask> AddAsync(AgentTask task,IReadOnlySet<string> grants)
    {
        Validate(task);long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if(task.ExpiresAt<=now||task.ExpiresAt>now+300000||task.State!="accepted")throw new Sx4Exception("expiry");
        if(!grants.Contains(task.Operation))throw new Sx4Exception("permission");
        await gate.WaitAsync();try
        {
            if(StorageError!=null)throw new Sx4Exception(StorageError);
            var duplicate=items.FirstOrDefault(x=>x.Id==task.Id);
            if(duplicate!=null){if(duplicate.DeviceId!=task.DeviceId||duplicate.Operation!=task.Operation||duplicate.Target!=task.Target)throw new Sx4Exception("request_collision");return duplicate;}
            if(items.Count>=256){var oldest=items.FindIndex(x=>x.State!="accepted"&&x.State!="running");if(oldest<0)throw new Sx4Exception("queue_full");items.RemoveAt(oldest);}
            items.Add(task);try{Save();}catch{items.Remove(task);StorageError="queue_write";throw;}
            return task;
        }finally{gate.Release();}
    }
    private readonly SemaphoreSlim workerGate=new(1);
    public async Task DrainAsync(Func<bool> session,Func<AgentTask,CancellationToken,Task<bool>> consent,Func<AgentTask,CancellationToken,Task> execute,IReadOnlySet<string> grants,CancellationToken cancel)
    {
        await workerGate.WaitAsync(cancel);
        try{
            foreach(var task in await SnapshotAsync())
            {
                if(task.State!="accepted")continue;
                if(StorageError!=null)throw new Sx4Exception(StorageError);
                if(task.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()){await TransitionAsync(task,"expired","");continue;}
                if(!grants.Contains(task.Operation)){await TransitionAsync(task,"cancelled","permission_revoked");continue;}
                if(!session())continue;
                using var expires=CancellationTokenSource.CreateLinkedTokenSource(cancel);
                expires.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1,task.ExpiresAt-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
                try{
                    if(NeedsConsent(task.Operation)&&!await consent(task,expires.Token)){await TransitionAsync(task,"cancelled","consent");continue;}
                    expires.Token.ThrowIfCancellationRequested();
                    if(!session())continue;
                    await TransitionAsync(task,"running","");
                    try{await execute(task,expires.Token);await TransitionAsync(task,"succeeded",task.Operation.StartsWith("steam.",StringComparison.Ordinal)?"steam_interaction_required":"");}
                    catch(OperationCanceledException){await TransitionAsync(task,"expired","");}
                    catch(Exception){await TransitionAsync(task,"failed","execution");}
                }catch(OperationCanceledException){await TransitionAsync(task,cancel.IsCancellationRequested?"cancelled":"expired","");}
            }
        }finally{workerGate.Release();}
    }
    private async Task TransitionAsync(AgentTask task,string state,string code)
    {
        await gate.WaitAsync();try{
            int index=items.FindIndex(x=>x.Id==task.Id);
            if(index<0)throw new Sx4Exception("queue_changed");
            items[index]=task with{State=state,Code=code};try{Save();}catch{StorageError="queue_write";throw;}
        }finally{gate.Release();}
        StateChanged?.Invoke(task with{State=state,Code=code});
    }
    public async Task CancelAllAsync(string reason)
    {
        await workerGate.WaitAsync();try{foreach(var task in await SnapshotAsync())if(task.State is "accepted" or "running")await TransitionAsync(task,"cancelled",reason);}
        finally{workerGate.Release();}
    }
    public async Task<string> RepairAsync()
    {
        await gate.WaitAsync();try{
            if(StorageError==null)throw new Sx4Exception("repair_not_needed");
            string backup=path+"."+Guid.NewGuid().ToString("N")+".bak";if(File.Exists(path))File.Copy(path,backup,false);
            items=[];Save();StorageError=null;return backup;
        }finally{gate.Release();}
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);byte[] plain=JsonSerializer.SerializeToUtf8Bytes(items);byte[] encrypted=protect(plain);
        string tmp=path+".tmp";try{using(var f=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(encrypted);f.Flush(true);}
            if(!unprotect(File.ReadAllBytes(tmp)).AsSpan().SequenceEqual(plain))throw new IOException("verify");File.Move(tmp,path,true);}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
}
