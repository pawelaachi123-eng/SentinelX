using System.Windows;
using System.Windows.Controls;

namespace SentinelX;
public partial class MainWindow
{
    private string lastSpeech = "";
    private DateTime lastSpeechAt;
    private int speechGeneration;
    private bool refreshingDevices, startingVoice;
    private readonly VoiceCommandInbox voiceCommands = new();
    private void DrainVoiceCommands()
    {
        if (busy || speaking || emergency || exiting || installing) return;
        if (voiceCommands.TryDequeue(out string command))
            _ = Dispatcher.BeginInvoke(new Action(async () => await ExecuteAsync(command, true)));
    }
    private async Task StartVoiceAsync()
    {
        if (emergency || installing || exiting || startingVoice) return;
        if (voice.IsListening) { SetStandby(); return; }
        startingVoice = true;
        try
        {
            if (!voice.IsReady) await voice.InitializeAsync(false, lifetime.Token);
            if (exiting || emergency) return;
            RefreshDevices();
            if (MicrophoneCombo.SelectedIndex < 0) throw new InvalidOperationException("Nie wykryto mikrofonu.");
            voice.SetWakeOnlyMode(true); voice.StartListening(MicrophoneCombo.SelectedIndex);
            ModelStatusText.Text = voice.CurrentAsrEngine + " gotowy.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { lastVoiceStatus = ex.Message; ModelStatusText.Text = ex.Message; ShowPage("Voice"); }
        finally { startingVoice = false; UpdateVoiceUi(); }
    }
    private void StopVoice() { voiceCommands.Clear(); voice.StopListening(); lastVoiceStatus = "Mikrofon wyłączony"; UpdateVoiceUi(); }
    private void SetStandby() { if (voice.IsListening) voice.SetWakeOnlyMode(true); activeUntil = default; UpdateVoiceUi(); }
    private void UpdateVoiceUi()
    {
        MicButton.Content = voice.IsListening ? voice.IsWakeOnlyMode ? "●  Czuwanie: Sentinel" : "●  Rozmowa aktywna" : "○  Mikrofon wyłączony";
        VoiceStartButton.Content = voice.IsListening ? "Wróć do czuwania" : "Włącz nasłuch";
        if (!busy) StatusText.Text = lastVoiceStatus;
    }
    private async void HandleSpeech(string text)
    {
        if (!voice.IsListening || emergency || speaking || exiting) return;
        if (text == lastSpeech && DateTime.Now - lastSpeechAt < TimeSpan.FromSeconds(2)) return;
        lastSpeech = text; lastSpeechAt = DateTime.Now;
        string rawCommand = RemoveWakeWord(text);
        string command = ConversationMemoryService.Normalize(rawCommand).TrimEnd('.', '?', '!', ' ');
        bool hasWake = !string.IsNullOrWhiteSpace(rawCommand) && rawCommand != text.Trim();
        if (voice.IsWakeOnlyMode)
        {
            if (!hasWake)
            {
                lastVoiceStatus = "Usłyszano bez wybudzenia: " + text;
                UpdateVoiceUi();
                return;
            }
            voice.SetWakeOnlyMode(false);
            activeUntil = DateTime.Now.AddMinutes(Math.Max(2, settings.Settings.ConversationMinutes));
            lastVoiceStatus = "Aktywna rozmowa. Wysyłam polecenie głosowe: " + rawCommand;
            UpdateVoiceUi();
        }
        if (string.IsNullOrWhiteSpace(command)) { lastVoiceStatus = "Wybudzono. Słucham następnego polecenia przez kilka minut."; UpdateVoiceUi(); if (settings.Settings.SpeakResponses) Speak("Słucham."); return; }
        lastVoiceStatus = "Wysyłam polecenie głosowe: " + rawCommand;
        UpdateVoiceUi();
        await ExecuteAsync(rawCommand, true);
    }
    private void RefreshDevices()
    {
        refreshingDevices = true;
        try
        {
            var microphones = voice.GetMicrophones(); int current = MicrophoneCombo.SelectedIndex;
            string saved = settings.Settings.SelectedMicrophoneName;
            int matched = saved.Length > 0 ? microphones.ToList().FindIndex(x => x[(x.IndexOf(':') + 1)..].Trim() == saved) : -1;
            MicrophoneCombo.ItemsSource = microphones;
            MicrophoneCombo.SelectedIndex = matched >= 0 ? matched : current >= 0 && current < microphones.Count ? current : voice.GetRecommendedMicrophoneDevice();
        }
        catch (Exception ex) { ModelStatusText.Text = "Nie udało się odczytać mikrofonów: " + ex.Message; }
        finally { refreshingDevices = false; }
    }
    private async void Microphone_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || refreshingDevices || MicrophoneCombo.SelectedIndex < 0) return;
        int selected = MicrophoneCombo.SelectedIndex; string name = MicrophoneCombo.SelectedItem?.ToString() ?? "";
        settings.Settings.SelectedMicrophoneDevice = selected;
        settings.Settings.SelectedMicrophoneName = name[(name.IndexOf(':') + 1)..].Trim(); settings.Save();
        if (voice.IsListening && voice.SelectedDeviceNumber != selected) { StopVoice(); await StartVoiceAsync(); }
    }
    private async Task InstallVoiceAsync(bool qwen)
    {
        if (installing || busy || emergency || startingVoice) return;
        installing = true; StopVoice();
        currentTask = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        InstallVoiceButton.IsEnabled = InstallQwenButton.IsEnabled = false;
        try
        {
            if (qwen) await voice.InstallQwenAsync(currentTask.Token); else await voice.InitializeAsync(true, currentTask.Token);
            ModelStatusText.Text = voice.CurrentAsrEngine + " gotowy. Możesz włączyć nasłuch.";
        }
        catch (OperationCanceledException) { ModelStatusText.Text = "Pobieranie przerwane. Możesz spróbować ponownie."; }
        catch (Exception ex) { ModelStatusText.Text = "Nie udało się przygotować modelu: " + ex.Message; AppLog.Write(ex); }
        finally { installing = false; currentTask.Dispose(); currentTask = null; InstallVoiceButton.IsEnabled = InstallQwenButton.IsEnabled = true; }
    }
    private void Speak(string text)
    {
        if (!speech.Available) { SpeechStatusText.Text = speech.Status; return; }
        speechGeneration++; speaking = true; voice.RecognitionSuppressed = true;
        speech.Speak(text, settings.Settings.SpeechVoice, settings.Settings.SpeechRate, settings.Settings.SpeechVolume);
    }
    private async void SpeechCompleted()
    {
        int generation = speechGeneration;
        try { await Task.Delay(350, lifetime.Token); } catch (OperationCanceledException) { return; }
        if (generation != speechGeneration || speech.IsSpeaking) return;
        speaking = false; voice.RecognitionSuppressed = false;
        activeUntil = DateTime.Now.AddMinutes(settings.Settings.ConversationMinutes);
        DrainVoiceCommands();
    }
    private void SpeechVoice_Changed(object sender, SelectionChangedEventArgs e) { if (!ready) return; settings.Settings.SpeechVoice = SpeechVoiceCombo.SelectedItem?.ToString() ?? ""; settings.Save(); }
    private async void Voice_Click(object sender, RoutedEventArgs e) => await StartVoiceAsync();
    private void MicOff_Click(object sender, RoutedEventArgs e) => StopVoice();
    private void Calibrate_Click(object sender, RoutedEventArgs e) { if (!voice.IsListening) ModelStatusText.Text = "Najpierw włącz mikrofon."; else voice.CalibrateNoise(); }
    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();
    private async void InstallVoice_Click(object sender, RoutedEventArgs e) => await InstallVoiceAsync(false);
    private async void InstallQwen_Click(object sender, RoutedEventArgs e) => await InstallVoiceAsync(true);
    private async void CheckVoice_Click(object sender, RoutedEventArgs e)
    { if (installing || startingVoice) return; try { await voice.InitializeAsync(false, lifetime.Token); ModelStatusText.Text = voice.CurrentAsrEngine + " gotowy."; } catch (Exception ex) { ModelStatusText.Text = ex.Message; } }
    private void TestSpeech_Click(object sender, RoutedEventArgs e) => Speak("Cześć. Jestem Sentinel. Słucham Cię.");
    private void StopSpeech_Click(object sender, RoutedEventArgs e) => speech.Stop();
}
