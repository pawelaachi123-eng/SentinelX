using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SentinelX.Services.Base;
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
}
finally{Directory.Delete(root,true);CryptographicOperations.ZeroMemory(secret);}
Console.WriteLine("PASS "+checks+" contract checks");
sealed class PartialStream(byte[] bytes):MemoryStream(bytes)
{public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)=>base.ReadAsync(buffer[..Math.Min(buffer.Length,3)],cancellationToken);}
