using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Services.Maintenance;
namespace SentinelX.ViewModels;
public partial class MaintenanceViewModel(MaintenanceService service):ObservableObject
{
 [ObservableProperty]private string result="";
 [ObservableProperty]private bool busy;
 [ObservableProperty]private string internetProbeUrl="";
 [ObservableProperty]private string publisherPublicKey="";
 [ObservableProperty]private string packageUrl="";
 [ObservableProperty]private string descriptorUrl="";
 [ObservableProperty]private bool requireSignature=true;
 [ObservableProperty]private string steamQuery="";
 [ObservableProperty]private SteamGame? selectedGame;
 public ObservableCollection<DiagnosticCheck> Checks{get;}=[];
 public ObservableCollection<SteamGame> Games{get;}=[];
 private async Task Run(Func<Task<string>> action){if(Busy)return;Busy=true;Result="Pracuję…";try{Result=await action();}catch(Exception e){Result=e is UpdateFailure x?"Aktualizacja odrzucona: "+x.Code:"Operacja nie powiodła się. Sprawdź lokalną konfigurację.";}finally{Busy=false;}}
 [RelayCommand]private Task DiagnoseAsync(CancellationToken cancel)=>Run(async()=>{var items=await service.DiagnoseAsync(InternetProbeUrl,cancel);Checks.Clear();foreach(var item in items)Checks.Add(item);return "Diagnostyka zakończona. BLOCKED wskazuje brak konfiguracji lub zewnętrznego komponentu.";});
 [RelayCommand]private Task RepairDirectoriesAsync()=>Run(service.RepairDirectoriesAsync);
 [RelayCommand]private Task RepairAutostartAsync()=>Run(()=>Task.FromResult(service.RepairAutostart()));
 [RelayCommand]private Task StageUpdateAsync(CancellationToken cancel)=>Run(()=>service.StageUpdateAsync(PublisherPublicKey,RequireSignature,cancel));
 [RelayCommand]private Task DownloadUpdateAsync(CancellationToken cancel)=>Run(()=>service.DownloadUpdateAsync(PackageUrl,DescriptorUrl,PublisherPublicKey,RequireSignature,null,cancel));
 [RelayCommand]private Task PreviewRepairAsync(CancellationToken cancel)=>Run(async()=>{var plan=await service.PreviewRepairAsync(cancel);return string.Join("\n",plan);});
 [RelayCommand]private Task ExportDiagnosticsAsync(CancellationToken cancel)=>Run(()=>service.ExportDiagnosticsAsync(cancel));
 [RelayCommand]private Task RollbackAsync()=>Run(()=>Task.Run(()=>{service.Updater.Rollback();return "Zweryfikowano rollback. Poprzednia wersja uruchomi się przy kolejnym starcie."; }));
 [RelayCommand]private Task ResolveSteamAsync()=>Run(async()=>{var items=await Task.Run(()=>service.ResolveSteam(SteamQuery));Games.Clear();foreach(var item in items)Games.Add(item);SelectedGame=null;return items.Count==0?"Nie znaleziono zainstalowanej gry. Użyj znanego numerycznego AppID w panelu Base.":"Wybierz dokładną grę. Jej AppID możesz skopiować do zadania Base.";});
 public string SelectedAppId=>SelectedGame?.AppId??"";
 partial void OnSelectedGameChanged(SteamGame? value)=>OnPropertyChanged(nameof(SelectedAppId));
}
