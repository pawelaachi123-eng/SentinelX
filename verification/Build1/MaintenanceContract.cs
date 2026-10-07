using System.IO.Compression;
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
   foreach(string path in new[]{"../escape","/outside","a\\b","a/../outside","C:/outside","a./bad"})Reject(()=>VerifiedUpdater.CanonicalChild(root,path),"path");
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
   string apps=Path.Combine(root,"Steam","steamapps");Directory.CreateDirectory(apps);
   File.WriteAllText(Path.Combine(apps,"appmanifest_10.acf"),"\"appid\" \"10\"\n\"name\" \"Same game Deluxe\"");
   File.WriteAllText(Path.Combine(apps,"appmanifest_11.acf"),"\"appid\" \"11\"\n\"name\" \"Same game Standard\"");
   File.WriteAllText(Path.Combine(apps,"appmanifest_12.acf"),"\"appid\" \"12;calc\"\n\"name\" \"Same game Shell\"");
   check(SteamResolver.Find("Same game",Path.GetDirectoryName(apps)!).Count==2,"Steam ambiguity retained and unsafe AppID rejected");
  }finally{Directory.Delete(root,true);}
 }
}
