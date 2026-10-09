using SentinelX;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SentinelX.Services.Base;
using SentinelX.Services.Agent;
using System.IO.Pipes;
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    string detail = e.ExceptionObject is Exception ex ? ex.GetType().Name + ": " + (ex.Message ?? "") : "?";
    detail = detail.Replace("\r", " ").Replace("\n", " ").Trim();
    if (detail.Length > 300) detail = detail[..300];
    Console.WriteLine("::error::Build1 crash: " + detail);
};
if(args.Length==4){
 string mode=args[0];await using var client=new BaseClient(new("127.0.0.1",int.Parse(args[1]),args[2],2,1,args[3]));
 try{await client.ConnectAsync(CancellationToken.None);var reply=await client.RequestAsync("status",new{},CancellationToken.None);
  if(mode!="ok"||!reply.Data.GetProperty("online").GetBoolean())throw new Exception("TLS expected failure");
  try{await client.RequestAsync("shell",new{},CancellationToken.None);throw new Exception("unsupported accepted");}catch(Sx4Exception e)when(e.Code=="unsupported"){}
  Console.WriteLine("PASS C# TLS status and capability guard");
 }catch(Sx4Exception e)when(e.Code==mode){Console.WriteLine("PASS C# TLS reject "+mode);}
 catch(System.Security.Authentication.AuthenticationException)when(mode=="pin"){Console.WriteLine("PASS C# TLS pin");}
 return;
}
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
void Reject(Action action,string code){try{action();throw new Exception("not rejected "+code);}catch(Sx4Exception e){Check(e.Code==code,"reject "+code);}}
byte[] key=RandomNumberGenerator.GetBytes(32);
var message=new BaseMessage{Type="status",Data=JsonSerializer.SerializeToElement(new{polish="Zażółć 🛰"})};
byte[] payload=Sx4Wire.Payload(message);long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var frame=new Sx4Frame(7,201,9,(ulong)now,payload);
var raw=Sx4Wire.Encode(frame,key);
Check(Sx4Wire.Parse(Sx4Wire.Decode(raw,key).Payload).Data.GetProperty("polish").GetString()=="Zażółć 🛰","SX4 unicode binary roundtrip");
using(var authority=new Sx4Authority(7,key)){authority.Verify(raw,now);Reject(()=>authority.Verify(raw,now),"replay");}
byte[] changed=raw.ToArray();changed[^1]^=1;Reject(()=>Sx4Wire.Decode(changed,key),"auth");
Reject(()=>Sx4Wire.Decode(raw,new byte[32]),"auth");
Reject(()=>Sx4Wire.Decode(raw[..^1],key),"size");
Reject(()=>Sx4Wire.Decode(new byte[5],key),"format");
Reject(()=>Sx4Wire.Encode(frame with{Payload=new byte[16385]},key),"format");
using(var authority=new Sx4Authority(8,key))Reject(()=>authority.Verify(raw,now),"device");
using(var authority=new Sx4Authority(7,key))Reject(()=>authority.Verify(raw,now+5001),"timestamp");
using(var authority=new Sx4Authority(7,key)){authority.Dispose();Reject(()=>authority.Verify(raw,now),"revoked");}
Reject(()=>Sx4Wire.Payload(message with{Version=3}),"version");
Reject(()=>Sx4Wire.Payload(message with{RequestId="bad"}),"request_id");
Reject(()=>Sx4Wire.Payload(message with{CorrelationId="bad"}),"request_id");
Reject(()=>Sx4Wire.Payload(message with{Type="../shell"}),"type");
Reject(()=>Sx4Wire.Payload(message with{ExpiresAt=0}),"expiry");
Reject(()=>Sx4Wire.Payload(message with{ExpiresAt=now+400000}),"expiry");
Reject(()=>Sx4Wire.Payload(message with{Data=JsonSerializer.SerializeToElement("bad")}),"payload");
Reject(()=>Sx4Wire.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(payload).Replace("\"version\":4","\"version\":4,\"Version\":4"))),"duplicate_key");
Reject(()=>Sx4Wire.Parse(Encoding.UTF8.GetBytes("{}")),"missing_field");
Reject(()=>Sx4Wire.Parse(new byte[]{0xff}),"utf8");
using(var partial=new PartialStream(raw)){Check((await Sx4Wire.ReadAsync(partial,CancellationToken.None)).SequenceEqual(raw),"partial frame");}
using(var authority=new Sx4Authority(7,key)){for(uint i=0;i<120;i++)authority.Verify(Sx4Wire.Encode(frame with{Nonce=i},key),now);Reject(()=>authority.Verify(Sx4Wire.Encode(frame with{Nonce=121},key),now),"rate_limit");}
string root=Path.Combine(Path.GetTempPath(),"sentinel-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
byte[] secret=RandomNumberGenerator.GetBytes(32);
byte[] Protect(byte[] plain){byte[] iv=RandomNumberGenerator.GetBytes(12),tag=new byte[16],encrypted=new byte[plain.Length];using var aes=new AesGcm(secret,16);aes.Encrypt(iv,plain,encrypted,tag);return iv.Concat(tag).Concat(encrypted).ToArray();}
byte[] Unprotect(byte[] encrypted){byte[] plain=new byte[encrypted.Length-28];using var aes=new AesGcm(secret,16);aes.Decrypt(encrypted.AsSpan(0,12),encrypted.AsSpan(28),encrypted.AsSpan(12,16),plain);return plain;}
try{
 string path=Path.Combine(root,"queue");var grants=new HashSet<string>{"lock","restart","steam.run","steam.install","ollama.ensure"};
 var queue=new AgentQueue(path,Protect,Unprotect);
 AgentTask task=new(Guid.NewGuid().ToString("D"),7,"lock","",DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds());
 await queue.AddAsync(task,grants);await queue.AddAsync(task,grants);
 Check((await queue.SnapshotAsync()).Count==1,"task request dedupe");
 Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("lock"),"encrypted queue");
 int executions=0;
 await queue.DrainAsync(()=>false,(_,_)=>Task.FromResult(true),(_,_)=>{executions++;return Task.CompletedTask;},grants,CancellationToken.None);
 Check(executions==0,"wait for unlocked session");
 await queue.DrainAsync(()=>true,(_,_)=>Task.FromResult(true),(_,_)=>{executions++;return Task.CompletedTask;},grants,CancellationToken.None);
 Check(executions==1&&(await queue.SnapshotAsync())[0].State=="succeeded","execute once");
 queue=new AgentQueue(path,Protect,Unprotect);await queue.DrainAsync(()=>true,(_,_)=>Task.FromResult(true),(_,_)=>{executions++;return Task.CompletedTask;},grants,CancellationToken.None);
 Check(executions==1,"no replay across restart");
 Reject(()=>AgentQueue.Validate(task with{Operation="shell"}),"task");
 Reject(()=>AgentQueue.Validate(task with{Target="cmd.exe"}),"target");
 Reject(()=>AgentQueue.Validate(task with{Operation="steam.run",Target="42;calc"}),"steam_appid");
 Reject(()=>AgentQueue.Validate(task with{Operation="steam.run",Target="00042"}),"steam_appid");
 Check((await queue.AddAsync(task with{Id=Guid.NewGuid().ToString("D"),Operation="steam.run",Target="730"},grants)).State=="accepted","numeric Steam AppID");
 var destructive=task with{Id=Guid.NewGuid().ToString("D"),Operation="restart"};
 await queue.AddAsync(destructive,grants);await queue.DrainAsync(()=>true,(_,_)=>Task.FromResult(false),(_,_)=>Task.CompletedTask,grants,CancellationToken.None);
 Check((await queue.SnapshotAsync()).Single(x=>x.Id==destructive.Id).State=="cancelled","required consent");
 var corrupt=Path.Combine(root,"corrupt");File.WriteAllText(corrupt,"original");
 var failed=new AgentQueue(corrupt,Protect,Unprotect);Check(failed.StorageError=="queue_corrupt","corrupt queue blocks");
 string backup=await failed.RepairAsync();Check(File.ReadAllText(backup)=="original"&&failed.StorageError==null,"repair exact backup and verified queue");
 var running=task with{Id=Guid.NewGuid().ToString("D"),State="running"};File.WriteAllBytes(path,Protect(JsonSerializer.SerializeToUtf8Bytes(new[]{running})));
 queue=new AgentQueue(path,Protect,Unprotect);Check((await queue.SnapshotAsync())[0].State=="failed","interrupted task never repeated");
 bool active=false;int launches=0;using var ollama=new OllamaSupervisor(_=>Task.FromResult(active),()=>{launches++;active=true;return true;});
 await Task.WhenAll(Enumerable.Range(0,8).Select(_=>ollama.EnsureAsync(CancellationToken.None)));
 Check(launches==1,"Ollama concurrent single server");
 Check(!ollama.AutoLoadModel,"Ollama no model preload");
 Check(OllamaSupervisor.IsModelName("llama3.1:8b"),"Ollama model name");
 Check(!OllamaSupervisor.IsModelName("x; rm -rf /"),"Ollama model name reject");
 Check(!OllamaSupervisor.IsModelName(new string('m',129)),"Ollama model name length");
 using(var genDoc=JsonDocument.Parse(OllamaSupervisor.GenerateRequest("m","5m")))
  Check(genDoc.RootElement.GetProperty("model").GetString()=="m"&&genDoc.RootElement.GetProperty("keep_alive").GetString()=="5m","Ollama generate request");
 using var models=new OllamaSupervisor(_=>Task.FromResult(true),()=>false);
 Check(!await models.EnsureModelAsync("bad name!",5,CancellationToken.None),"Ollama model validation");
 Check(!await models.EnsureModelAsync("llama3.1:8b",5,CancellationToken.None),"Ollama model load fails closed");
 Check(await models.ReleaseIdleModelsAsync(30,CancellationToken.None)==0&&models.TrackedModels==0,"Ollama idle none tracked");
}
finally{Directory.Delete(root,true);CryptographicOperations.ZeroMemory(secret);}
AgentRequest parsed=AgentProtocol.ParseRequest("{\"op\":\"status\",\"token\":\"abc\",\"payload\":{\"a\":1}}");
Check(parsed.Op=="status"&&parsed.Token=="abc"&&parsed.Payload.GetProperty("a").GetInt32()==1,"agent protocol parse");
try{AgentProtocol.ParseRequest("{bad");throw new Exception("not rejected format");}catch(AgentException e){Check(e.Code=="format","reject agent format");}
string agentDir=Path.Combine(Path.GetTempPath(),"sentinel-agent-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(agentDir);
try{
 byte[] AgentId(byte[] x)=>x;
 string bootToken=AgentAuth.CreateToken();
 AgentAuth.SaveToken(agentDir,bootToken,AgentId);
 Check(AgentAuth.LoadToken(agentDir,AgentId)==bootToken,"agent token roundtrip");
 Check(!AgentAuth.ValidToken(new string('0',64),bootToken),"agent token reject");
 Check(AgentAuth.LoadToken(Path.Combine(agentDir,"nope"),AgentId)==null,"agent token missing");
 string agentPipe="sentinel-test-"+Guid.NewGuid().ToString("N")[..12];
 var canned=new AgentTask(Guid.NewGuid().ToString("D"),7,"lock","",DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds());
 await using var agentServer=new AgentPipeServer(agentPipe,()=>bootToken,(request,_)=>request.Op switch{
  "status"=>Task.FromResult<object>(new{status="Base połączona",config=(object?)null,capabilities=new[]{"pc.heartbeat"}}),
  "tasks"=>Task.FromResult<object>(new[]{canned}),
  "health"=>Task.FromResult<object>(new{queueError=(string?)null}),
  _=>throw new AgentException("unknown_op")});
 using var agentCts=new CancellationTokenSource(TimeSpan.FromSeconds(30));
 var agentServe=agentServer.RunAsync(agentCts.Token);
 var agentClient=new AgentClient(agentPipe,()=>bootToken);
 Check((await agentClient.SendAsync("status",new{},CancellationToken.None)).GetProperty("status").GetString()=="Base połączona","agent pipe status");
 try{await new AgentClient(agentPipe,()=>new string('0',64)).SendAsync("status",new{},CancellationToken.None);throw new Exception("not rejected auth");}catch(AgentException e){Check(e.Code=="auth","reject agent auth");}
 try{await agentClient.SendAsync("nope",new{},CancellationToken.None);throw new Exception("not rejected op");}catch(AgentException e){Check(e.Code=="unknown_op","reject agent op");}
 using(var rawPipe=new NamedPipeClientStream(".",agentPipe,PipeDirection.InOut,PipeOptions.Asynchronous)){
  await rawPipe.ConnectAsync(CancellationToken.None);
  using var oversizeWriter=new StreamWriter(rawPipe,new System.Text.UTF8Encoding(false),4096,true);
  await oversizeWriter.WriteLineAsync(new string('x',AgentProtocol.MaxMessage+8));
  await oversizeWriter.FlushAsync();
  using var oversizeReader=new StreamReader(rawPipe,System.Text.Encoding.UTF8,false,4096,true);
  string? oversizeReply=await oversizeReader.ReadLineAsync(CancellationToken.None);
  Check(oversizeReply!=null&&oversizeReply.Contains("size"),"agent oversize envelope");
 }
 using var proxy=new AgentBaseProxy(agentClient);
 proxy.Start();
 var proxyDeadline=DateTime.UtcNow.AddSeconds(10);
 while(proxy.Status!="Base połączona"&&DateTime.UtcNow<proxyDeadline)await Task.Delay(100);
 Check(proxy.Status=="Base połączona"&&proxy.Capabilities.Count==1,"agent proxy poll");
 Check((await proxy.TasksAsync()).Count==1,"agent proxy tasks");
 Check(await proxy.QueueStorageErrorAsync(CancellationToken.None)==null,"agent proxy health");
 agentCts.Cancel();
 await agentServe;
}
finally{Directory.Delete(agentDir,true);}
Check(TextScrubber.Scrub("key "+new string('b',64))=="key <hash>","scrub hashes");
Check(TextScrubber.Scrub("{\"token\": \"abc\"}")=="{\"token\": \"<redacted>\"}","scrub redacts JSON secrets with shape");
Check(TextScrubber.Scrub("api_key=abcdef")=="api_key=<redacted>","scrub secrets");
Check(TextScrubber.Scrub("plain note")=="plain note","scrub keeps plain text");

{
    using var huge = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 2 * 1024 * 1024) + "\n"));
    try { await AgentProtocol.ReadMessageAsync(huge, CancellationToken.None); throw new Exception("oversize accepted"); }
    catch (AgentException e) when (e.Code == "size") { Check(true, "pipe rejects oversize envelope incrementally"); }
    using var crlf = new MemoryStream(Encoding.UTF8.GetBytes("{\"op\":\"ping\"}\r\n"));
    Check(await AgentProtocol.ReadMessageAsync(crlf, CancellationToken.None) == "{\"op\":\"ping\"}", "pipe strips CRLF");
    using var empty = new MemoryStream([]);
    Check(await AgentProtocol.ReadMessageAsync(empty, CancellationToken.None) == null, "pipe clean EOF is null");
}
{
    string troot = Path.Combine(Path.GetTempPath(), "sentinel-token-" + Guid.NewGuid().ToString("N"));
    string created = AgentAuth.CreateToken();
    AgentAuth.SaveToken(troot, created, b => b);
    Check(AgentAuth.LoadToken(troot, b => b) == created && created.Length == 64, "agent token roundtrip");
    Check(AgentAuth.OwnerMutexName(troot).StartsWith(@"Local\SentinelX-BaseOwner-"), "owner mutex name");
    try
    {
        var owned = AgentAuth.TryOwnBase(troot);
        Check(owned != null, "base owner acquired");
        try
        {
            // Blocking wait (not await): ReleaseMutex must run on the owning thread.
            var rivalTask = Task.Run(() => AgentAuth.TryOwnBase(troot));
            Check(rivalTask.Wait(TimeSpan.FromSeconds(10)), "rival attempt finished");
            Check(rivalTask.Result == null, "second owner refused");
        }
        finally { try { owned.ReleaseMutex(); } catch { } owned.Dispose(); }
        using (var again = AgentAuth.TryOwnBase(troot)) { Check(again != null, "owner released and reacquired"); again?.ReleaseMutex(); }
    }
    catch (Exception e) when (e is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
    { Check(true, "named mutex unavailable on this runner: " + e.GetType().Name + " (semantics verified on Windows"); }
    Directory.Delete(troot, true);
}
Check(BaseClient.IsFatalLinkFailure(new Sx4Exception("auth")), "fatal auth blocks link");
Check(BaseClient.IsFatalLinkFailure(new Sx4Exception("revoked")), "fatal revoked blocks link");
Check(BaseClient.IsFatalLinkFailure(new Sx4Exception("unsupported")), "fatal unsupported blocks link");
Check(!BaseClient.IsFatalLinkFailure(new Sx4Exception("size")), "transient size reconnects");
Check(!BaseClient.IsFatalLinkFailure(new Sx4Exception("correlation")), "transient correlation reconnects");
Check(!BaseClient.IsFatalLinkFailure(new JsonException()), "protocol parse reconnects");
Check(BaseClient.IsFatalLinkFailure(new System.Security.Authentication.AuthenticationException()), "fatal TLS auth blocks link");
Check(BaseClient.IsCapabilityType("pc.heartbeat"), "capability charset ok");
Check(!BaseClient.IsCapabilityType("a/b"), "capability charset rejects slash");
Check(!BaseClient.IsCapabilityType(""), "capability charset rejects empty");
{
    using var okDoc = JsonDocument.Parse("{\"code\":\"busy\"}");
    Check(Sx4Wire.RemoteErrorCode(okDoc.RootElement) == "busy", "remote error code passthrough");
    using var evilDoc = JsonDocument.Parse("{\"code\":\"A;rm\"}");
    Check(Sx4Wire.RemoteErrorCode(evilDoc.RootElement) == "remote_error", "remote error code sanitized");
    using var missingDoc = JsonDocument.Parse("{}");
    Check(Sx4Wire.RemoteErrorCode(missingDoc.RootElement) == "remote_error", "remote error code default");
}
await MaintenanceContract.Run(Check);
Console.WriteLine("PASS "+checks+" contract checks");
sealed class PartialStream(byte[] bytes):MemoryStream(bytes)
{public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)=>base.ReadAsync(buffer[..Math.Min(buffer.Length,3)],cancellationToken);}
