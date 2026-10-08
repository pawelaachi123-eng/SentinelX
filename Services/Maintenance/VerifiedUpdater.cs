using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SentinelX.Services.Maintenance;
public sealed class UpdateFailure(string code):Exception(code){public string Code{get;}=code;}
public sealed record UpdateDescriptor(string Version,string Architecture,string Sha256,long Size,string? Signature=null)
{
 public static UpdateDescriptor Parse(string json){
  if(json.Length>16384)throw new UpdateFailure("metadata_size");
  using var doc=JsonDocument.Parse(json,new(){MaxDepth=4});var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var p in doc.RootElement.EnumerateObject())if(!names.Add(p.Name))throw new UpdateFailure("metadata_duplicate");
  return JsonSerializer.Deserialize<UpdateDescriptor>(json,new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})??throw new UpdateFailure("metadata");
 }
 public string SignedText=>$"SXUP1\n{Version}\n{Architecture}\n{Sha256.ToLowerInvariant()}\n{Size}\n";
}
public sealed record UpdateVersion(string Version,string Directory,Dictionary<string,string> Hashes);
public sealed record UpdateJournal(UpdateVersion? Active=null,UpdateVersion? LastGood=null,int LaunchFailures=0,long LastLaunch=0);
/// Stages complete portable packages. The descriptor is consumed; this service never generates an update manifest.
public sealed class VerifiedUpdater
{
 private readonly string root;private readonly Func<string,bool> validatePackage;private readonly Func<string,string?>? packageVersion;private readonly SemaphoreSlim gate=new(1);
 public VerifiedUpdater(string root,Func<string,bool> validatePackage,Func<string,string?>? packageVersion=null){this.root=Path.GetFullPath(root);this.validatePackage=validatePackage;this.packageVersion=packageVersion;}
 public static Version Semver(string value){
  var match=Regex.Match(value??"",@"^([0-9]+)\.([0-9]+)\.([0-9]+)(?:\+[0-9A-Za-z.-]+)?$");
  if(!match.Success||Enumerable.Range(1,3).Any(i=>match.Groups[i].Value.Length>1&&match.Groups[i].Value[0]=='0'))throw new UpdateFailure("semver");
  try{return new(int.Parse(match.Groups[1].Value),int.Parse(match.Groups[2].Value),int.Parse(match.Groups[3].Value));}catch(FormatException){throw new UpdateFailure("semver");}catch(OverflowException){throw new UpdateFailure("semver");}
 }
 public static void VerifyDescriptor(UpdateDescriptor descriptor,string current,string? publicPem,bool requireSignature){
  if(Semver(descriptor.Version)<=Semver(current))throw new UpdateFailure("downgrade");
  if(descriptor.Architecture!="win-x64")throw new UpdateFailure("architecture");
  if(descriptor.Size is <1024 or >536870912||!Regex.IsMatch(descriptor.Sha256??"","^[0-9a-fA-F]{64}$"))throw new UpdateFailure("metadata");
  if(requireSignature){
   if(string.IsNullOrWhiteSpace(publicPem)||string.IsNullOrWhiteSpace(descriptor.Signature))throw new UpdateFailure("signature_required");
   try{using var rsa=RSA.Create();rsa.ImportFromPem(publicPem);
    if(rsa.KeySize<2048||!rsa.VerifyData(System.Text.Encoding.UTF8.GetBytes(descriptor.SignedText),Convert.FromBase64String(descriptor.Signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new UpdateFailure("signature");
   }catch(Exception e)when(e is CryptographicException or FormatException or ArgumentException){throw new UpdateFailure("signature");}
  }
 }
 public static string CanonicalChild(string root,string relative){
  if(string.IsNullOrWhiteSpace(relative)||relative.Contains('\\')||relative.Any(c=>c is ':' or '*' or '?' or '<' or '>' or '|'||char.IsControl(c))||relative.Split('/').Any(x=>x is ".." or "."||x.EndsWith(' ')||x.EndsWith('.')||Regex.IsMatch(x.Split('.')[0],"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",RegexOptions.IgnoreCase)))throw new UpdateFailure("path");
  string full=Path.GetFullPath(Path.Combine(root,relative)),prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new UpdateFailure("path");return full;
 }
 public async Task<UpdateVersion> StageAsync(string zip,UpdateDescriptor descriptor,string current,string? publicPem,bool requireSignature,long freeBytes,CancellationToken cancel){
  VerifyDescriptor(descriptor,current,publicPem,requireSignature);
  if(freeBytes<descriptor.Size*4+134217728)throw new UpdateFailure("disk_space");
  if(new FileInfo(zip).Length!=descriptor.Size)throw new UpdateFailure("size");
  await using(var stream=File.OpenRead(zip)){string hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,cancel));if(!hash.Equals(descriptor.Sha256,StringComparison.OrdinalIgnoreCase))throw new UpdateFailure("hash");}
  await gate.WaitAsync(cancel);string stage=Path.Combine(root,"staging-"+Guid.NewGuid().ToString("N"));
  try{
   Directory.CreateDirectory(stage);using var archive=ZipFile.OpenRead(zip);
   if(archive.Entries.Count is <1 or >2048)throw new UpdateFailure("entries");
   long expanded=0;var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var entry in archive.Entries){
    cancel.ThrowIfCancellationRequested();string path=CanonicalChild(stage,entry.FullName.TrimEnd('/'));
    if(!paths.Add(path)||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new UpdateFailure("path");
    expanded=checked(expanded+entry.Length);if(expanded>2147483648||expanded>freeBytes-67108864)throw new UpdateFailure("expanded_size");
    if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(path);continue;}
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await using var input=entry.Open();await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);
    await input.CopyToAsync(output,cancel);await output.FlushAsync(cancel);
   }
   if(!validatePackage(stage))throw new UpdateFailure("package");
   if(packageVersion!=null&&Semver(packageVersion(stage)??"")!=Semver(descriptor.Version))throw new UpdateFailure("package_version");
   var version=new UpdateVersion(descriptor.Version,stage,Inventory(stage));
   string final=Path.Combine(root,"versions",descriptor.Version.Split('+')[0]+"-"+descriptor.Sha256[..16].ToLowerInvariant());
   Directory.CreateDirectory(Path.GetDirectoryName(final)!);if(Directory.Exists(final))throw new UpdateFailure("already_staged");Directory.Move(stage,final);
   return version with{Directory=final};
  }finally{if(Directory.Exists(stage))Directory.Delete(stage,true);gate.Release();}
 }
 public static Dictionary<string,string> Inventory(string folder){
  var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(var path in Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories)){
   if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new UpdateFailure("path");
   using var input=File.OpenRead(path);hashes.Add(Path.GetRelativePath(folder,path).Replace('\\','/'),Convert.ToHexString(SHA256.HashData(input)));
  }return hashes;
 }
 public static bool VerifyVersion(UpdateVersion version){
  try{
   if(!Directory.Exists(version.Directory)||version.Hashes.Count==0)return false;
   if(Directory.EnumerateFiles(version.Directory,"*",SearchOption.AllDirectories).Count()!=version.Hashes.Count)return false;
   foreach(var entry in version.Hashes){using var input=File.OpenRead(CanonicalChild(version.Directory,entry.Key));if(Convert.ToHexString(SHA256.HashData(input))!=entry.Value)return false;}return true;
  }catch(Exception e)when(e is IOException or UnauthorizedAccessException or UpdateFailure){return false;}
 }
 public UpdateJournal ReadJournal(){
  string path=Path.Combine(root,"state.json");if(!File.Exists(path))return new();
  if(new FileInfo(path).Length>2097152)throw new UpdateFailure("state");
  return JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(path))??throw new UpdateFailure("state");
 }
 private void Save(UpdateJournal state){
  Directory.CreateDirectory(root);string path=Path.Combine(root,"state.json"),temp=path+".tmp";
  using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(file,state);file.Flush(true);}
  if(JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(temp))==null)throw new UpdateFailure("state_verify");File.Move(temp,path,true);
 }
 public async Task ActivateAsync(UpdateVersion candidate,UpdateVersion current,Func<string,CancellationToken,Task<bool>> health,CancellationToken cancel){
  await gate.WaitAsync(cancel);
  try{
   if(!VerifyVersion(candidate)||!VerifyVersion(current))throw new UpdateFailure("inventory");
   using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);deadline.CancelAfter(TimeSpan.FromMinutes(4));
   bool good;try{good=await health(candidate.Directory,deadline.Token);}catch(OperationCanceledException){good=false;}
   if(!good||!VerifyVersion(candidate))throw new UpdateFailure("health");
   Save(new(candidate,current));
  }finally{gate.Release();}
 }
 public UpdateVersion? BeginLaunch(){
  var state=ReadJournal();if(state.Active==null)return null;
  long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();int failures=now-state.LastLaunch<=300000?state.LaunchFailures+1:1;
  if(!VerifyVersion(state.Active)||failures>=3){
   if(state.LastGood==null||!VerifyVersion(state.LastGood))throw new UpdateFailure("rollback_unavailable");
   Save(new(state.LastGood,null));return state.LastGood;
  }
  Save(state with{LaunchFailures=failures,LastLaunch=now});return state.Active;
 }
 public void MarkHealthy(string directory){
  var state=ReadJournal();if(state.Active!=null&&Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).Equals(state.Active.Directory.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&VerifyVersion(state.Active))
   Save(state with{LaunchFailures=0,LastLaunch=0});
 }
 public void NoteUnhealthy(){
  var state=ReadJournal();if(state.Active==null)return;
  Save(state with{LaunchFailures=state.LaunchFailures+1,LastLaunch=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});
 }
 public UpdateVersion Rollback(){
  var state=ReadJournal();if(state.LastGood==null||!VerifyVersion(state.LastGood))throw new UpdateFailure("rollback_unavailable");
  Save(new(state.LastGood,null));return state.LastGood;
 }
}
