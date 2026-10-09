using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
#if !WINUI3
using System.Windows;
#endif
using Microsoft.Win32;
#if WINUI3
using SentinelX.Services.Maintenance;
#endif
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
  void Skip(string name,string detail)=>checks.Add(new(name,"SKIPPED",detail));
  var settingsHealth=new AppSettingsService();
  string dpapiDetail;bool dpapiOk;
  try{
   byte[] probe=RandomNumberGenerator.GetBytes(32);
   byte[] roundtrip=ProtectedData.Unprotect(ProtectedData.Protect(probe,null,DataProtectionScope.CurrentUser),null,DataProtectionScope.CurrentUser);
   dpapiOk=probe.AsSpan().SequenceEqual(roundtrip);
   dpapiDetail=dpapiOk?"Magazyn Windows odpowiada (próba szyfrowania)":"Próba szyfrowania nie powiodła się";
   string identity=Path.Combine(AppPaths.Root,"Base","identity.bin");
   if(dpapiOk&&File.Exists(identity)){
    try{ProtectedData.Unprotect(File.ReadAllBytes(identity),null,DataProtectionScope.CurrentUser);dpapiDetail+="; parowanie odszyfrowane";}
    catch(Exception e)when(e is CryptographicException or IOException){dpapiOk=false;dpapiDetail="Plik parowania nie odszyfrowuje się — zarchiwizuj go z ekranu odzyskiwania i sparuj ponownie";}
   }
  }catch(Exception e)when(e is CryptographicException or IOException){dpapiOk=false;dpapiDetail="Magazyn Windows niedostępny";}
  string? queueError=null;try{queueError=await agent.QueueStorageErrorAsync(cancel);}catch(Exception e)when(e is IOException or AgentException or OperationCanceledException){queueError="unreachable";}
  await Task.Run(()=>{
   Check("Autostart",new StartupService("Sentinel X").IsEnabled(),"Rejestr użytkownika; bez stałego administratora");
   Check("Sieć",NetworkInterface.GetIsNetworkAvailable(),"Interfejs sieci");
   NetworkInterface[] adapters;
   try{adapters=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up).ToArray();}
   catch(NetworkInformationException){adapters=[];}
   Check("Gateway",adapters.Any(x=>{try{return x.GetIPProperties().GatewayAddresses.Count>0;}catch(NetworkInformationException){return false;}}),"Gateway skonfigurowany");
   Check("DNS",adapters.Any(x=>{try{return x.GetIPProperties().DnsAddresses.Count>0;}catch(NetworkInformationException){return false;}}),"DNS skonfigurowany; brak wysyłania rozmów");
   bool apipa=adapters.SelectMany(x=>{try{return x.GetIPProperties().UnicastAddresses;}catch(NetworkInformationException){return Enumerable.Empty<UnicastIPAddressInformation>();}}).Any(x=>x.Address.ToString().StartsWith("169.254.",StringComparison.Ordinal));
   Check("DHCP",!apipa,apipa?"Wykryto adres APIPA — router nie przydzielił adresu":"Brak adresów APIPA");
   string? gateway=adapters.SelectMany(x=>{try{return x.GetIPProperties().GatewayAddresses;}catch(NetworkInformationException){return Enumerable.Empty<GatewayIPAddressInformation>();}}).Select(x=>x.Address.ToString()).FirstOrDefault(x=>!string.IsNullOrEmpty(x));
   if(gateway==null)Skip("Router","Brak gateway do sprawdzenia");
   else{
    try{using var ping=new Ping();var reply=ping.Send(gateway,1500);Check("Router",reply.Status==IPStatus.Success,"Gateway "+gateway+": "+reply.Status+" "+reply.RoundtripTime+" ms");}
    catch(Exception e)when(e is PingException or InvalidOperationException){Check("Router",false,"Gateway "+gateway+" nie odpowiada");}
   }
   Check("Dysk",new DriveInfo(Path.GetPathRoot(AppPaths.Root)!).AvailableFreeSpace>536870912,"Wymagane co najmniej 512 MiB wolnego");
   Check("Konfiguracja",settingsHealth.LastError==null,settingsHealth.LastError??"Ustawienia sparsowane i zwalidowane względem schematu");
   Check("Base/auth/SX4",agent.Capabilities.Count>0,agent.Status);
   Check("DPAPI",dpapiOk,dpapiDetail);
   var relayMode=agent.PublicConfiguration?.Mode;
   if(relayMode is "vpn" or "relay"){
    var relayConfig=agent.PublicConfiguration!;
    try{
     using var tcp=new System.Net.Sockets.TcpClient();
     var connected=tcp.ConnectAsync(relayConfig.Host,relayConfig.Port);
     if(connected.Wait(TimeSpan.FromSeconds(3)))Check("Relay/VPN",tcp.Connected,"Endpoint "+relayConfig.Host+":"+relayConfig.Port+" osiągalny (TCP; bez logowania)");
     else Check("Relay/VPN",false,"Endpoint "+relayConfig.Host+":"+relayConfig.Port+" nie odpowiada w 3 s");
    }catch(Exception e)when(e is System.Net.Sockets.SocketException or InvalidOperationException or ArgumentException){Check("Relay/VPN",false,"Endpoint nieosiągalny");}
   }
   else Skip("Relay/VPN","Tryb Base: "+(relayMode??"brak parowania")+"; test endpointu tylko dla vpn/relay");
   Check("Kolejka",queueError==null,queueError==null?"Magazyn kolejki odszyfrowany i spójny":"Kolejka: "+queueError);
   Check("Steam",File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam","steam.exe")),"Lokalna instalacja");
   string logProbe=Path.Combine(AppPaths.LogsDirectory,".probe");bool logsWritable=false;
   try{Directory.CreateDirectory(AppPaths.LogsDirectory);File.WriteAllText(logProbe,"probe");File.Delete(logProbe);logsWritable=true;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
   long errorBytes=0;try{string errors=Path.Combine(AppPaths.LogsDirectory,"errors.log");if(File.Exists(errors))errorBytes=new FileInfo(errors).Length;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
   Check("Logi",logsWritable,"Zapis do katalogu logów "+(logsWritable?"działa":"NIEDOSTĘPNY")+"; errors.log "+errorBytes+" B; dziennik Base ma limit 8 MiB");
   var state=Updater.ReadJournal();Check("Crash-loop",state.LaunchFailures<3,"Licznik: "+state.LaunchFailures);
   Check("Aktualizacje",state.LaunchFailures<3,"Aktywna: "+(state.Active?.Version??"brak")+"; ostatnia dobra: "+(state.LastGood?.Version??"brak"));
   var runs=RunHealth.Read(AppPaths.Root,"ui");var agentRuns=RunHealth.Read(AppPaths.Root,"agent");Check("Uruchomienia",runs.ConsecutiveFailures==0&&agentRuns.ConsecutiveFailures==0,"Nieczyste starty UI/Agent: "+runs.ConsecutiveFailures+"/"+agentRuns.ConsecutiveFailures);

  },cancel);
  if(agent.PublicConfiguration==null)Skip("Android","Base nie jest sparowana");
  else{
   try{
    using var deviceTimeout=CancellationTokenSource.CreateLinkedTokenSource(cancel);deviceTimeout.CancelAfter(TimeSpan.FromSeconds(10));
    var devices=await agent.RequestAsync("devices/status",new{},deviceTimeout.Token);
    if(devices.Data.TryGetProperty("devices",out var list)&&list.ValueKind==JsonValueKind.Array)
     Check("Android",true,"Urządzenia przez Base: "+list.GetArrayLength());
    else Check("Android",true,"Base odpowiada na devices/status");
   }catch(Sx4Exception e){Check("Android",false,"Base: "+e.Code);}
   catch(Exception e)when(e is IOException or OperationCanceledException){Check("Android",false,"Zapytanie devices/status nie powiodło się");}
  }
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
 public async Task<IReadOnlyList<string>> PreviewRepairAsync(CancellationToken cancel){
  var plan=new List<string>();
  foreach(var dir in new[]{AppPaths.SettingsDirectory,AppPaths.LogsDirectory,AppPaths.HistoryDirectory,AppPaths.MemoryDirectory,AppPaths.CacheDirectory})
   plan.Add((Directory.Exists(dir)?"OK ":"UTWORZĘ ")+dir);
  string? previewQueue;try{previewQueue=await agent.QueueStorageErrorAsync(cancel);}catch{previewQueue="unreachable";}
  plan.Add(previewQueue==null?"OK kolejka spójna":"NAPRAWIĘ kolejkę (kopia + odbudowa): "+previewQueue);
  await Task.Run(()=>{
   string? autostart=null;
   try{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",false);autostart=key?.GetValue("Sentinel X")?.ToString();}catch{}
   plan.Add(autostart==null?"UTWORZĘ wpis autostartu Sentinel X":"OK wpis autostartu istnieje");
   try{var journal=Updater.ReadJournal();plan.Add(journal.Active==null?"OK brak oczekującej aktualizacji":"INFO aktywna aktualizacja "+journal.Active.Version+" (rollback dostępny: "+(journal.LastGood!=null?"tak":"nie")+")");}catch(Exception e){plan.Add("UWAGA dziennik aktualizacji nieczytelny: "+e.GetType().Name);}
  },cancel);
  return plan;
 }
 public async Task<string> ExportDiagnosticsAsync(CancellationToken cancel){
  #if WINUI3
  var dialogPath = await WinUiFileDialog.SaveJsonAsync();
  if (dialogPath == null) return "Anulowano";
#else
  var dialog=new SaveFileDialog{Filter="Diagnostyka|*.json",FileName="sentinel-diagnostics.json"};if(dialog.ShowDialog()!=true)return "Anulowano";
  var dialogPath = dialog.FileName;
#endif
  var checks=await DiagnoseAsync("",cancel);
  string json=JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,version=AppConstants.SemanticVersion,checks},new JsonSerializerOptions{WriteIndented=true});
  await File.WriteAllTextAsync(dialogPath,DiagnosticRedaction.Redact(json),cancel);
  return "Zapisano zredagowaną diagnostykę: "+dialogPath;
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
  #if WINUI3
  var zipPath = await WinUiFileDialog.OpenFileAsync(".zip");
  if (zipPath == null) return "Anulowano";
#else
  var zip=new OpenFileDialog{Filter="Portable package|*.zip",CheckFileExists=true};if(zip.ShowDialog()!=true)return "Anulowano";
  var zipPath = zip.FileName;
#endif
  #if WINUI3
  var metadataPath = await WinUiFileDialog.OpenFileAsync(".json");
  if (metadataPath == null) return "Anulowano";
#else
  var metadata=new OpenFileDialog{Filter="Opis aktualizacji|*.json",CheckFileExists=true};if(metadata.ShowDialog()!=true)return "Anulowano";
  var metadataPath = metadata.FileName;
#endif
  return await InstallUpdateAsync(zipPath,metadataPath,publicPem,requireSignature,cancel);
 }
 public async Task<string> DownloadUpdateAsync(string packageUrl,string descriptorUrl,string publicPem,bool requireSignature,IProgress<double>? progress,CancellationToken cancel){
  UpdateDownloader.ValidateUrl(packageUrl);UpdateDownloader.ValidateUrl(descriptorUrl);
  using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=System.Threading.Timeout.InfiniteTimeSpan};
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
