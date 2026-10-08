using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using SentinelX.Core;
using SentinelX.Services.Agent;
using SentinelX.Services.Base;
namespace SentinelX.Services.Maintenance;
public sealed record DiagnosticCheck(string Name,string State,string Detail);
public sealed class MaintenanceService(IBaseControl agent,OllamaSupervisor ollama)
{
 public VerifiedUpdater Updater{get;}=new(Path.Combine(AppPaths.Root,"Updates"),ValidPackage,folder=>FileVersionInfo.GetVersionInfo(Path.Combine(folder,"SentinelX.exe")).ProductVersion);
 public async Task<IReadOnlyList<DiagnosticCheck>> DiagnoseAsync(string internetProbe,CancellationToken cancel){
  var checks=new List<DiagnosticCheck>();
  void Check(string name,bool ok,string detail)=>checks.Add(new(name,ok?"PASS":"BLOCKED",detail));
  await Task.Run(()=>{
   Check("Autostart",new StartupService("Sentinel X").IsEnabled(),"Rejestr użytkownika; bez stałego administratora");
   Check("Sieć",NetworkInterface.GetIsNetworkAvailable(),"Interfejs sieci");
   var adapters=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up).ToArray();
   Check("Gateway",adapters.Any(x=>x.GetIPProperties().GatewayAddresses.Count>0),"Gateway skonfigurowany");
   Check("DNS",adapters.Any(x=>x.GetIPProperties().DnsAddresses.Count>0),"DNS skonfigurowany; brak wysyłania rozmów");
   Check("Dysk",new DriveInfo(Path.GetPathRoot(AppPaths.Root)!).AvailableFreeSpace>536870912,"Wymagane co najmniej 512 MiB wolnego");
   Check("Konfiguracja",Directory.Exists(AppPaths.SettingsDirectory),"Katalog ustawień");
   Check("Base/auth/SX4",agent.Capabilities.Count>0,agent.Status);
   Check("DPAPI",agent.PublicConfiguration!=null,"Sekret nie jest odczytywany do raportu");
   Check("Relay/VPN",agent.PublicConfiguration?.Mode is "vpn" or "relay","Wymaga własnego skonfigurowanego endpointu");
   Check("Steam",File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam","steam.exe")),"Lokalna instalacja");
   Check("Logi",Directory.Exists(AppPaths.LogsDirectory),"Lokalne logi; dziennik Base ma limit 8 MiB");
   var state=Updater.ReadJournal();Check("Crash-loop",state.LaunchFailures<3,"Licznik: "+state.LaunchFailures);

   Check("Android",false,"Stan telefonu jest dostępny przez devices/status Base");
  },cancel);
  if(string.IsNullOrWhiteSpace(internetProbe))checks.Add(new("Internet","SKIPPED","Podaj opcjonalny HTTPS endpoint, aby sprawdzić połączenie"));
  else {
   if(!Uri.TryCreate(internetProbe,UriKind.Absolute,out var address)||address.Scheme!="https"||!string.IsNullOrEmpty(address.UserInfo))throw new ArgumentException("probe_url");
   try{using var http=new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(3)};
    using var request=new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head,address);using var response=await http.SendAsync(request,System.Net.Http.HttpCompletionOption.ResponseHeadersRead,cancel);
    Check("Internet",response.IsSuccessStatusCode,"Wskazany HTTPS endpoint odpowiedział: "+(int)response.StatusCode);
   }catch(System.Net.Http.HttpRequestException){Check("Internet",false,"Wskazany endpoint niedostępny");}catch(TaskCanceledException)when(!cancel.IsCancellationRequested){Check("Internet",false,"Timeout 3 s");}
  }
  Check("Ollama",await ollama.StatusAsync(cancel),"Tylko loopback /api/tags; bez ładowania modelu");
  return checks;
 }
 public async Task<string> RepairDirectoriesAsync()=>await Task.Run(()=>"Sprawdzono katalogi. Kopia i dziennik: "+DirectoryRepair.Run([AppPaths.SettingsDirectory,AppPaths.LogsDirectory,AppPaths.HistoryDirectory,AppPaths.MemoryDirectory,AppPaths.CacheDirectory],AppPaths.BackupsDirectory));
 public string RepairAutostart(){
  string backup=Path.Combine(AppPaths.BackupsDirectory,"startup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);string log=Path.Combine(backup,"repair.jsonl");
  void Record(string phase,string result)=>File.AppendAllText(log,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,phase,result})+"\n");
  const string path=@"Software\Microsoft\Windows\CurrentVersion\Run";
  using var key=Registry.CurrentUser.CreateSubKey(path,true);if(key==null)throw new IOException("registry");object? previous=key.GetValue("Sentinel X");
  Record("detect","started");File.WriteAllText(Path.Combine(backup,"previous.json"),JsonSerializer.Serialize(previous));Record("backup","succeeded");
  if(new StartupService("Sentinel X").Enable()){Record("repair","succeeded");Record("verify","succeeded");Record("result","succeeded");return "Autostart zapisany i zweryfikowany";}
  if(previous==null)key.DeleteValue("Sentinel X",false);else key.SetValue("Sentinel X",previous,RegistryValueKind.String);
  Record("rollback",Equals(key.GetValue("Sentinel X"),previous)?"succeeded":"failed");Record("result","failed");return "Autostart nie został potwierdzony; przywrócono poprzedni wpis";
 }
 public IReadOnlyList<SteamGame> ResolveSteam(string query)=>SteamResolver.Find(query,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
 public static bool ValidPackage(string folder){
  try{
   string exe=Path.Combine(folder,"SentinelX.exe");using var file=File.OpenRead(exe);using var reader=new BinaryReader(file);
   if(reader.ReadUInt16()!=0x5a4d)return false;file.Position=0x3c;int offset=reader.ReadInt32();if(offset<64||offset>file.Length-6)return false;file.Position=offset;
   if(reader.ReadUInt32()!=0x4550||reader.ReadUInt16()!=0x8664||!File.Exists(Path.Combine(folder,"coreclr.dll")))return false;
   return true;
  }catch(Exception e)when(e is IOException or UnauthorizedAccessException){return false;}
 }
 public static async Task<bool> SmokeAsync(string folder,CancellationToken cancel){
  string results=Path.Combine(AppPaths.CacheDirectory,"update-health-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(results);
  using var p=Process.Start(new ProcessStartInfo(Path.Combine(folder,"SentinelX.exe")){ArgumentList={"--ui-smoke",results},UseShellExecute=false,WorkingDirectory=folder});
  if(p==null)return false;
  try{await p.WaitForExitAsync(cancel);return p.ExitCode==0&&File.Exists(Path.Combine(results,"ui-smoke.txt"));}
  catch(OperationCanceledException){if(!p.HasExited)p.Kill(true);return false;}
 }
 public async Task<string> StageUpdateAsync(string publicPem,bool requireSignature,CancellationToken cancel){
  var zip=new OpenFileDialog{Filter="Portable package|*.zip",CheckFileExists=true};if(zip.ShowDialog()!=true)return "Anulowano";
  var metadata=new OpenFileDialog{Filter="Opis aktualizacji|*.json",CheckFileExists=true};if(metadata.ShowDialog()!=true)return "Anulowano";
  return await InstallUpdateAsync(zip.FileName,metadata.FileName,publicPem,requireSignature,cancel);
 }
 public async Task<string> DownloadUpdateAsync(string packageUrl,string descriptorUrl,string publicPem,bool requireSignature,IProgress<double>? progress,CancellationToken cancel){
  UpdateDownloader.ValidateUrl(packageUrl);UpdateDownloader.ValidateUrl(descriptorUrl);
  using var http=new HttpClient{Timeout=System.Threading.Timeout.InfiniteTimeSpan};
  string descriptorJson=await UpdateDownloader.DownloadStringAsync(http,descriptorUrl,16384,cancel);
  var descriptor=UpdateDescriptor.Parse(descriptorJson);
  string downloads=Path.Combine(AppPaths.Root,"Updates","downloads");Directory.CreateDirectory(downloads);
  string package=Path.Combine(downloads,"update-"+descriptor.Sha256[..16].ToLowerInvariant()+".zip");
  string descriptorPath=package+".json";await File.WriteAllTextAsync(descriptorPath,descriptorJson,cancel);
  await UpdateDownloader.DownloadAsync(http,packageUrl,package,descriptor.Size,progress,cancel);
  return await InstallUpdateAsync(package,descriptorPath,publicPem,requireSignature,cancel);
 }
 public async Task<string> InstallUpdateAsync(string package,string description,string publicPem,bool requireSignature,CancellationToken cancel){
  return await Task.Run(async()=>{
   if(new FileInfo(description).Length>16384)throw new UpdateFailure("metadata_size");
   var descriptor=UpdateDescriptor.Parse(await File.ReadAllTextAsync(description,cancel));
   var deadline=DateTime.UtcNow.AddSeconds(60);
   while(DateTime.UtcNow<deadline){cancel.ThrowIfCancellationRequested();if((await agent.TasksAsync()).All(x=>x.State!="running"))break;await Task.Delay(2000,cancel);}
   if((await agent.TasksAsync()).Any(x=>x.State=="running"))throw new UpdateFailure("busy");
   await StopAgentForUpdateAsync(cancel);
   string backup=BackupUpdateState(descriptor);
   long free=new DriveInfo(Path.GetPathRoot(AppPaths.Root)!).AvailableFreeSpace;
   var staged=await Updater.StageAsync(package,descriptor,AppConstants.SemanticVersion,publicPem,requireSignature,free,cancel);
   string shipped=AppContext.BaseDirectory;
   var current=new UpdateVersion(AppConstants.SemanticVersion,shipped,VerifiedUpdater.Inventory(shipped));
   await Updater.ActivateAsync(staged,current,SmokeAsync,cancel);
   return "Zweryfikowano pakiet i smoke test; kopia: "+backup+". Aktualizacja uruchomi się przy kolejnym starcie Sentinel.";
  },cancel);
 }
 public string BackupUpdateState(UpdateDescriptor descriptor){
  string backup=Path.Combine(AppPaths.BackupsDirectory,"update-"+descriptor.Version.Split('+')[0]+"-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
  Directory.CreateDirectory(backup);
  foreach(var dir in new[]{AppPaths.SettingsDirectory,Path.Combine(AppPaths.Root,"Base")}){
   if(!Directory.Exists(dir))continue;
   foreach(var file in Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories)){
    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;
    string target=Path.Combine(backup,Path.GetFileName(dir),Path.GetRelativePath(dir,file));
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.Copy(file,target,true);
   }
  }
  bool hadIdentity=File.Exists(Path.Combine(AppPaths.Root,"Base","identity.bin"));
  File.WriteAllText(Path.Combine(backup,"pairing.json"),JsonSerializer.Serialize(new{version=descriptor.Version,hadIdentity,time=DateTimeOffset.UtcNow}));
  return backup;
 }
 public async Task<string> ConfirmHealthyAsync(CancellationToken cancel){
  var journal=Updater.ReadJournal();
  string current=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
  if(journal.Active==null)return "Brak oczekującej aktualizacji; stan stabilny.";
  if(!current.Equals(journal.Active.Directory.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))
   return "Aktualizacja "+journal.Active.Version+" gotowa; uruchom Sentinel ponownie, aby ją zastosować.";
  var problems=new List<string>();
  if(!VerifiedUpdater.VerifyVersion(journal.Active))problems.Add("inventory");
  if(new AppSettingsService().LastError!=null)problems.Add("settings");
  try{
   string? queueError=await agent.QueueStorageErrorAsync(cancel);
   if(queueError!=null)problems.Add("queue:"+queueError);
  }catch(Exception e)when(e is IOException or AgentException or OperationCanceledException){problems.Add("queue_unreachable");}
  if(Directory.Exists(AppPaths.BackupsDirectory)){
   string? pairingFile=Directory.EnumerateFiles(AppPaths.BackupsDirectory,"pairing.json",SearchOption.AllDirectories)
    .Where(x=>Path.GetDirectoryName(x)!.StartsWith(Path.Combine(AppPaths.BackupsDirectory,"update-"+journal.Active.Version.Split('+')[0]+"-"),StringComparison.OrdinalIgnoreCase))
    .OrderByDescending(x=>x).FirstOrDefault();
   if(pairingFile!=null){
    try{
     using var doc=JsonDocument.Parse(File.ReadAllText(pairingFile));
     if(doc.RootElement.TryGetProperty("hadIdentity",out var had)&&had.ValueKind==JsonValueKind.True&&agent.PublicConfiguration==null)
      problems.Add("pairing_missing");
    }catch(Exception e)when(e is IOException or JsonException){}
   }
  }
  if(problems.Count==0){Updater.MarkHealthy(current);return "Wersja "+journal.Active.Version+" potwierdzona: inwentarz, ustawienia, kolejka i parowanie sprawne.";}
  try{Updater.NoteUnhealthy();}catch(Exception e)when(e is IOException or UpdateFailure){}
  return "Wersja niezdrowa ("+string.Join(",",problems)+"); odnotowano awarię — rollback po 3 niezdrowych startach.";
 }
 private static async Task StopAgentForUpdateAsync(CancellationToken cancel){
  if(!AgentClient.IsAgentRunning(AppPaths.Root))return;
  string? token=AgentAuth.LoadToken(AppPaths.Root,value=>ProtectedData.Unprotect(value,null,DataProtectionScope.CurrentUser));
  if(token==null)throw new UpdateFailure("agent_running");
  await new AgentClient(AgentAuth.PipeName(AppPaths.Root),()=>token).SendAsync("stop",new{},cancel);
  var deadline=DateTime.UtcNow.AddSeconds(40);
  while(AgentClient.IsAgentRunning(AppPaths.Root)){
   if(DateTime.UtcNow>=deadline)throw new UpdateFailure("agent_running");
   await Task.Delay(500,cancel);
  }
 }
}
