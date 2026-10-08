using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using SentinelX.Services.Maintenance;
static class MaintenanceContract
{
 public static async Task Run(Action<bool,string> check){
  void Reject(Action action,string code){try{action();throw new Exception("accepted "+code);}catch(UpdateFailure e){check(e.Code==code,"updater rejects "+code);}}
  async Task RejectAsync(Func<Task> action,string code){try{await action();throw new Exception("accepted "+code);}catch(UpdateFailure e){check(e.Code==code,"updater rejects "+code);}}
  string root=Path.Combine(Path.GetTempPath(),"sentinel-update-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   string zip=Path.Combine(root,"package.zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){var entry=archive.CreateEntry("SentinelX.exe");using var output=entry.Open();output.Write(RandomNumberGenerator.GetBytes(8192));}
   var meta=new UpdateDescriptor("2.0.0","win-x64",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))),new FileInfo(zip).Length);
   Reject(()=>VerifiedUpdater.VerifyDescriptor(meta with{Version="1.0.0"},"1.0.0",null,false),"downgrade");
   Reject(()=>VerifiedUpdater.VerifyDescriptor(meta with{Version="02.0.0"},"1.0.0",null,false),"semver");
   Reject(()=>VerifiedUpdater.VerifyDescriptor(meta with{Architecture="win-arm64"},"1.0.0",null,false),"architecture");
   Reject(()=>VerifiedUpdater.VerifyDescriptor(meta,"1.0.0",null,true),"signature_required");
   Reject(()=>UpdateDescriptor.Parse("{\"Version\":\"2.0.0\",\"version\":\"3.0.0\"}"),"metadata_duplicate");
   foreach(string path in new[]{"../escape","/outside","a\\b","a/../outside","C:/outside","a./bad","CON.txt","a/<bad>"})Reject(()=>VerifiedUpdater.CanonicalChild(root,path),"path");
   using var rsa=RSA.Create(2048);string publicKey=rsa.ExportSubjectPublicKeyInfoPem();
   string signature=Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(meta.SignedText),HashAlgorithmName.SHA256,RSASignaturePadding.Pss));
   VerifiedUpdater.VerifyDescriptor(meta with{Signature=signature},"1.0.0",publicKey,true);check(true,"updater publisher RSA-PSS signature");
   Reject(()=>VerifiedUpdater.VerifyDescriptor(meta with{Signature=Convert.ToBase64String(new byte[256])},"1.0.0",publicKey,true),"signature");
   var manager=new VerifiedUpdater(Path.Combine(root,"updates"),_=>true,_=>"2.0.0");
   await RejectAsync(()=>manager.StageAsync(zip,meta,"1.0.0",null,false,10,CancellationToken.None),"disk_space");
   await RejectAsync(()=>manager.StageAsync(zip,meta with{Sha256=new string('0',64)},"1.0.0",null,false,long.MaxValue/2,CancellationToken.None),"hash");
   var mismatch=new VerifiedUpdater(Path.Combine(root,"wrong-version"),_=>true,_=>"1.0.0");
   await RejectAsync(()=>mismatch.StageAsync(zip,meta,"1.0.0",null,false,long.MaxValue/2,CancellationToken.None),"package_version");
   var candidate=await manager.StageAsync(zip,meta,"1.0.0",null,false,long.MaxValue/2,CancellationToken.None);
   check(VerifiedUpdater.VerifyVersion(candidate),"updater staged inventory verified");
   string baseline=Path.Combine(root,"current");Directory.CreateDirectory(baseline);File.WriteAllText(Path.Combine(baseline,"original"),"last good");
   var current=new UpdateVersion("1.0.0",baseline,VerifiedUpdater.Inventory(baseline));
   await RejectAsync(()=>manager.ActivateAsync(candidate,current,(_,_)=>Task.FromResult(false),CancellationToken.None),"health");
   check(manager.ReadJournal().Active==null,"failed health never activated");
   await manager.ActivateAsync(candidate,current,(_,_)=>Task.FromResult(true),CancellationToken.None);
   check(manager.ReadJournal().Active?.Version=="2.0.0","good health activates");
   manager.BeginLaunch();manager.BeginLaunch();check(manager.BeginLaunch()?.Version=="1.0.0","crash loop rolls back to last good");
   await manager.ActivateAsync(candidate,current,(_,_)=>Task.FromResult(true),CancellationToken.None);
   File.AppendAllText(Path.Combine(candidate.Directory,"SentinelX.exe"),"tampered");check(!VerifiedUpdater.VerifyVersion(candidate),"changed staged executable rejected");
   check(manager.BeginLaunch()?.Version=="1.0.0","corrupt active version rolls back");
   string traversal=Path.Combine(root,"traversal.zip");using(var archive=ZipFile.Open(traversal,ZipArchiveMode.Create)){using var output=archive.CreateEntry("../escape").Open();output.Write(RandomNumberGenerator.GetBytes(8192));}
   var dangerous=meta with{Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(traversal))),Size=new FileInfo(traversal).Length};
   await RejectAsync(()=>manager.StageAsync(traversal,dangerous,"1.0.0",null,false,long.MaxValue/2,CancellationToken.None),"path");
   string first=Path.Combine(root,"repair-one"),second=Path.Combine(root,"repair-two");
   try{DirectoryRepair.Run([first,second],Path.Combine(root,"backups"),p=>{if(p==second)throw new IOException("injected");Directory.CreateDirectory(p);});throw new Exception("repair fault accepted");}catch(IOException){}
   check(!Directory.Exists(first)&&!Directory.Exists(second),"directory repair rolls back only newly created empty folders");
   check(Directory.EnumerateFiles(Path.Combine(root,"backups"),"repair.jsonl",SearchOption.AllDirectories).Select(File.ReadAllText).Any(x=>x.Contains("rollback")&&x.Contains("succeeded")),"repair phases persisted");
   string original=Path.Combine(root,"original-data");Directory.CreateDirectory(original);File.WriteAllText(Path.Combine(original,"user"),"keep");
   DirectoryRepair.Run([original,first],Path.Combine(root,"backups"));
   check(File.ReadAllText(Path.Combine(original,"user"))=="keep"&&Directory.Exists(first),"repair preserves existing user data and verifies directories");
   var sick=new VerifiedUpdater(Path.Combine(root,"sick-updates"),_=>true,_=>"2.0.0");
   var candidate2=await sick.StageAsync(zip,meta,"1.0.0",null,false,long.MaxValue/2,CancellationToken.None);
   await sick.ActivateAsync(candidate2,current,(_,_)=>Task.FromResult(true),CancellationToken.None);
   sick.NoteUnhealthy();sick.NoteUnhealthy();check(sick.ReadJournal().LaunchFailures==2,"sick launches counted");
   sick.NoteUnhealthy();check(sick.BeginLaunch()?.Version=="1.0.0","three sick launches roll back");
   int calls=0;byte[] body=Encoding.UTF8.GetBytes(new string('u',4096));
   var flaky=new ScriptHandler(_=>{calls++;return calls<3?new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError):new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(body)};});
   using var testHttp=new HttpClient(flaky);
   string package=Path.Combine(root,"dl.zip");
   await UpdateDownloader.DownloadAsync(testHttp,"https://updates.example/sentinel.zip",package,body.Length,null,CancellationToken.None);
   check(calls==3&&File.ReadAllBytes(package).SequenceEqual(body),"download retries then verifies size");
   File.WriteAllBytes(package,body[..1024]);string? range=null;
   var resume=new ScriptHandler(req=>{range=req.Headers.Range?.ToString();
    var partial=new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent){Content=new ByteArrayContent(body[1024..])};
    partial.Content.Headers.ContentRange=new System.Net.Http.Headers.ContentRangeHeaderValue(1024,body.Length-1,body.Length);
    return partial;});
   using var resumeHttp=new HttpClient(resume);
   await UpdateDownloader.DownloadAsync(resumeHttp,"https://updates.example/sentinel.zip",package,body.Length,null,CancellationToken.None);
   check(range=="bytes=1024-"&&File.ReadAllBytes(package).SequenceEqual(body),"download resumes from partial file");
   await RejectAsync(()=>UpdateDownloader.DownloadAsync(resumeHttp,"http://updates.example/x",package,body.Length,null,CancellationToken.None),"url");
   var text=new ScriptHandler(_=>new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent("{\"Version\":\"2.0.0\"}")});
   using var textHttp=new HttpClient(text);
   check((await UpdateDownloader.DownloadStringAsync(textHttp,"https://updates.example/meta.json",16384,CancellationToken.None)).Contains("2.0.0"),"descriptor download");
   string runs=Path.Combine(root,"runs");
   var first=RunHealth.BeginRun(runs,"ui");check(first.ConsecutiveFailures==0&&!first.CleanExit,"first run starts dirty");
   RunHealth.EndRun(runs,"ui");check(RunHealth.Read(runs,"ui").CleanExit,"clean exit recorded");
   var second=RunHealth.BeginRun(runs,"ui");check(second.ConsecutiveFailures==0,"clean previous resets counter");
   var third=RunHealth.BeginRun(runs,"ui");check(third.ConsecutiveFailures==1,"crash counted");
   File.WriteAllText(RunHealth.StatePath(runs,"ui"),"{broken");
   check(RunHealth.Read(runs,"ui").ConsecutiveFailures==1&&!File.Exists(RunHealth.StatePath(runs,"ui")),"corrupt state fails closed and is archived");
   string apps=Path.Combine(root,"Steam","steamapps");Directory.CreateDirectory(apps);
   File.WriteAllText(Path.Combine(apps,"appmanifest_10.acf"),"\"appid\" \"10\"\n\"name\" \"Same game Deluxe\"");
   File.WriteAllText(Path.Combine(apps,"appmanifest_11.acf"),"\"appid\" \"11\"\n\"name\" \"Same game Standard\"");
   File.WriteAllText(Path.Combine(apps,"appmanifest_12.acf"),"\"appid\" \"12;calc\"\n\"name\" \"Same game Shell\"");
   check(SteamResolver.Find("Same game",Path.GetDirectoryName(apps)!).Count==2,"Steam ambiguity retained and unsafe AppID rejected");
  }finally{Directory.Delete(root,true);}
 }
}
sealed class ScriptHandler(Func<HttpRequestMessage,HttpResponseMessage> script):HttpMessageHandler
{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)=>Task.FromResult(script(request));}
