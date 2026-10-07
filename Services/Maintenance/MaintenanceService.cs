using System.IO;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using SentinelX.Core;
using SentinelX.Services.Base;
namespace SentinelX.Services.Maintenance;
public sealed record DiagnosticCheck(string Name,string State,string Detail);
public sealed class MaintenanceService(WindowsBaseService agent,OllamaSupervisor ollama)
{
 public VerifiedUpdater Updater{get;}=new(Path.Combine(AppPaths.Root,"Updates"),ValidPackage,folder=>FileVersionInfo.GetVersionInfo(Path.Combine(folder,"SentinelX.exe")).ProductVersion);
 public async Task<IReadOnlyList<DiagnosticCheck>> DiagnoseAsync(CancellationToken cancel){
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
   Check("Internet",false,"Sprawdź żądaniem do własnego relayu; diagnostyka offline nie wysyła danych na zewnętrzny serwer");
   Check("Android",false,"Stan telefonu jest dostępny przez devices/status Base");
  },cancel);
  Check("Ollama",await ollama.StatusAsync(cancel),"Tylko loopback /api/tags; bez ładowania modelu");
  return checks;
 }
 public async Task<string> RepairDirectoriesAsync(){
  return await Task.Run(()=>{
   string backup=Path.Combine(AppPaths.BackupsDirectory,"repair-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
   string log=Path.Combine(backup,"repair.jsonl");
   void Record(string phase,string state)=>File.AppendAllText(log,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,phase,state})+"\n");
   string[] folders=[AppPaths.SettingsDirectory,AppPaths.LogsDirectory,AppPaths.HistoryDirectory,AppPaths.MemoryDirectory,AppPaths.CacheDirectory];
   Record("detect","started");File.WriteAllText(Path.Combine(backup,"directories.json"),JsonSerializer.Serialize(folders.Select(p=>new{path=Path.GetFileName(p),exists=Directory.Exists(p)})));Record("backup","succeeded");
   try{foreach(string folder in folders)Directory.CreateDirectory(folder);Record("repair","succeeded");
    if(folders.Any(p=>!Directory.Exists(p)))throw new IOException("verify");Record("verify","succeeded");Record("result","succeeded");return "Sprawdzono katalogi. Kopia i dziennik: "+backup;
   }catch{Record("rollback","no_user_data_changed");Record("result","failed");throw;}
  });
 }
 public string RepairAutostart()=>new StartupService("Sentinel X").Enable()?"Autostart zapisany i zweryfikowany":"Nie udało się potwierdzić autostartu";
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
  string package=zip.FileName,description=metadata.FileName;
  return await Task.Run(async()=>{
   if(new FileInfo(description).Length>16384)throw new UpdateFailure("metadata_size");
   var descriptor=UpdateDescriptor.Parse(await File.ReadAllTextAsync(description,cancel));
   long free=new DriveInfo(Path.GetPathRoot(AppPaths.Root)!).AvailableFreeSpace;
   var staged=await Updater.StageAsync(package,descriptor,AppConstants.SemanticVersion,publicPem,requireSignature,free,cancel);
   string shipped=AppContext.BaseDirectory;
   var current=new UpdateVersion(AppConstants.SemanticVersion,shipped,VerifiedUpdater.Inventory(shipped));
   await Updater.ActivateAsync(staged,current,SmokeAsync,cancel);
   return "Zweryfikowano pakiet i smoke test. Aktualizacja uruchomi się przy kolejnym starcie Sentinel.";
  },cancel);
 }
}
