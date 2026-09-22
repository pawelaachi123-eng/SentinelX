using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Voice;
using SentinelX.Services.Settings;
namespace SentinelX.ViewModels;
public partial class VoiceViewModel : ObservableObject, IDisposable
{
    private readonly IVoiceService voice;
    private readonly ISettingsService settings;
    private readonly IUiDispatcher dispatcher;
    public ObservableCollection<string> Microphones { get; } = [];
    [ObservableProperty] private int selectedMicrophone;
    [ObservableProperty] private string status = "Mikrofon wyłączony";
    [ObservableProperty] private VoiceState state;
    [ObservableProperty] private VoiceMetrics metrics = new(VoiceState.Off, 0, 0, 0, 0, 0, false, "");
    public VoiceViewModel(IVoiceService voice, ISettingsService settings, IUiDispatcher dispatcher)
    {
        this.voice = voice; this.settings = settings; this.dispatcher = dispatcher;
        voice.Changed += Sync; voice.MetricsUpdated += Update;
        SelectedMicrophone = settings.Current.Voice.SelectedMicrophoneDevice;
        RefreshMicrophones();
    }
    private void Sync() => dispatcher.Post(() => { State = voice.State; Status = voice.Status; });
    private void Update(VoiceMetrics value) => dispatcher.Post(() => Metrics = value);
    [RelayCommand] private void RefreshMicrophones()
    {
        try
        {
            int selected = SelectedMicrophone; Microphones.Clear();
            foreach (var mic in voice.GetMicrophones()) Microphones.Add(mic);
            SelectedMicrophone = Microphones.Count == 0 ? -1 : Math.Clamp(selected, 0, Microphones.Count - 1);
            if (Microphones.Count == 0) Status = "Windows nie wykrywa mikrofonu.";
        }
        catch (Exception ex) { Status = "Nie można odczytać urządzeń: " + ex.Message; }
    }
    [RelayCommand(IncludeCancelCommand = true)] private Task StartAsync(CancellationToken token) => StartCoreAsync(false, token);
    [RelayCommand(IncludeCancelCommand = true)] private Task DownloadModelsAsync(CancellationToken token) => StartCoreAsync(true, token);
    private async Task StartCoreAsync(bool download, CancellationToken token)
    {
        if (SelectedMicrophone < 0) { Status = "Wybierz dostępny mikrofon."; return; }
        try
        {
            settings.Current.Voice.SelectedMicrophoneDevice = SelectedMicrophone;
            settings.Current.Voice.SelectedMicrophoneName = Microphones.ElementAtOrDefault(SelectedMicrophone) ?? "";
            settings.Save();
            await voice.StartAsync(SelectedMicrophone, download, token);
        }
        catch (OperationCanceledException) { Status = "Przerwano uruchamianie głosu."; }
        catch (Exception ex) { Status = ex.Message; AppLog.Write(ex); }
    }
    [RelayCommand] private void Stop() => voice.Stop();
    [RelayCommand] private void Calibrate() => voice.Calibrate();
    public void Dispose() { voice.Changed -= Sync; voice.MetricsUpdated -= Update; }
}
