using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Base;
namespace SentinelX.ViewModels;
public partial class BaseViewModel : ObservableObject,IDisposable
{
    private readonly WindowsBaseService service;private readonly IUiDispatcher ui;
    [ObservableProperty]private string host="";
    [ObservableProperty]private int port=443;
    [ObservableProperty]private string fingerprint="";
    [ObservableProperty]private string secret="";
    [ObservableProperty]private int deviceId=2;
    [ObservableProperty]private int baseId=1;
    [ObservableProperty]private string mode="lan";
    [ObservableProperty]private string status="";
    [ObservableProperty]private string result="";
    [ObservableProperty]private bool allowPowerActions;
    [ObservableProperty]private string targetId="";
    [ObservableProperty]private string timerOperation="lock";
    [ObservableProperty]private int timerSeconds=30;
    [ObservableProperty]private string steamAppId="730";
    [ObservableProperty]private DiscoveredBase? selectedBase;
    public string[] Modes{get;}=["lan","vpn","relay"];
    public string[] TimerOperations{get;}=["lock","sentinel.show","ollama.ensure"];
    public ObservableCollection<DiscoveredBase> Bases{get;}=[];
    public ObservableCollection<AgentTask> Tasks{get;}=[];
    public BaseViewModel(WindowsBaseService service,IUiDispatcher ui)
    {
        this.service=service;this.ui=ui;service.Changed+=Sync;Status=service.Status;
        if(service.PublicConfiguration is{} c){Host=c.Host;Port=c.Port;Fingerprint=c.Fingerprint;DeviceId=c.DeviceId;BaseId=c.BaseId;Mode=c.Mode;}
    }
    private void Sync()=>ui.Post(()=>Status=service.Status);
    partial void OnSelectedBaseChanged(DiscoveredBase? value){if(value==null)return;Host=value.Host;Port=value.Port;BaseId=value.BaseId;Result="Odkrycie nie potwierdza tożsamości. Porównaj fingerprint z Base przed parowaniem."; }
    private async Task Run(Func<Task> action){try{Status="Pracuję…";await action();}catch(Exception e){Result=e is Sx4Exception x?"Błąd Base: "+x.Code:"Operacja nie powiodła się. Sprawdź konfigurację lub diagnostykę.";}finally{Status=service.Status;}}
    [RelayCommand]private Task DiscoverAsync(CancellationToken cancel)=>Run(async()=>{var found=await BaseDiscovery.DiscoverAsync(cancel);Bases.Clear();foreach(var item in found)Bases.Add(item);Result="Odkryto: "+found.Count+". Porównaj fingerprint z fizyczną Base.";});
    [RelayCommand]private Task PairAsync(CancellationToken cancel)=>Run(async()=>{
        var grants=new List<string>{"lock","steam.run","ollama.ensure","sentinel.show","notification"};
        if(AllowPowerActions)grants.AddRange(["sleep","restart","shutdown","steam.install"]);
        await service.PairAsync(new(Host.Trim(),Port,Fingerprint.Trim(),checked((ushort)DeviceId),checked((ushort)BaseId),Secret.Trim(),Mode,grants.ToArray()),cancel);
        Secret="";Result="Parowanie potwierdzone. Sekret zapisano w magazynie Windows.";});
    [RelayCommand]private Task UnpairAsync()=>Run(async()=>{await service.UnpairAsync();Secret="";Result="Parowanie usunięte z PC. Unieważnij ten sekret także w Base.";});
    [RelayCommand]private Task RefreshTasksAsync()=>Run(async()=>{var tasks=await service.TasksAsync();Tasks.Clear();foreach(var task in tasks)Tasks.Add(task);Result="Zadania Agenta: "+tasks.Count;});
    [RelayCommand]private Task QueryAsync(string type,CancellationToken cancel)=>Run(async()=>{var response=await service.RequestAsync(type,new{},cancel);Result=response.Data.ToString();});
    [RelayCommand]private Task RunSceneAsync(CancellationToken cancel)=>Run(async()=>{ValidateTarget(TargetId);using var expiry=CancellationTokenSource.CreateLinkedTokenSource(cancel);expiry.CancelAfter(TimeSpan.FromSeconds(30));
        if(!await WindowsBaseService.ConfirmAsync(new(Guid.NewGuid().ToString("D"),checked((ushort)DeviceId),"scene",TargetId,DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds()),expiry.Token)){Result="Anulowano scenę";return;}
        Result=(await service.RequestAsync("scenes.run",new{id=TargetId,expiresSeconds=30},expiry.Token)).Data.ToString();});
    [RelayCommand]private Task WakeAsync(CancellationToken cancel)=>Run(async()=>{ValidateTarget(TargetId);Result=(await service.RequestAsync("wol",new{pcId=TargetId},cancel)).Data.ToString();});
    [RelayCommand]private Task SetTimerAsync(CancellationToken cancel)=>Run(async()=>{
        if(TimerSeconds is <1 or >300||!TimerOperations.Contains(TimerOperation))throw new Sx4Exception("timer");
        ValidateTarget(TargetId);Result=(await service.RequestAsync("timers.create",new{pcId=TargetId,operation=TimerOperation,seconds=TimerSeconds},cancel)).Data.ToString();});
    [RelayCommand]private Task LaunchSteamAsync(CancellationToken cancel)=>Run(async()=>{
        if(!uint.TryParse(SteamAppId,out var id)||id==0||id.ToString()!=SteamAppId)throw new Sx4Exception("steam_appid");
        ValidateTarget(TargetId);Result=(await service.RequestAsync("tasks.create",new{pcId=TargetId,operation="steam.run",target=SteamAppId,expiresSeconds=60},cancel)).Data.ToString();});
    [RelayCommand]private Task RepairQueueAsync()=>Run(async()=>{Result="Naprawiono kolejkę. Kopia: "+await service.RepairQueueAsync();});
    private static void ValidateTarget(string value){if(value.Length is <1 or >64||value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('_' or '-')))throw new Sx4Exception("target");}
    public void Dispose()=>service.Changed-=Sync;
}
