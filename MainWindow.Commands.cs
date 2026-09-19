using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace SentinelX;
public partial class MainWindow
{
    private static string Normalize(string text) => ConversationMemoryService.Normalize(text).Trim().TrimEnd('.', '?', '!');
    private static string RemoveWakeWord(string text) => Regex.Replace(text.Trim(), @"^(?:hej\s+)?(?:sentinel|sentynel|sentinelu|sentynelu|centinel|centynel|centenel|santinel|sentnel)(?:\s*x)?(?=[\s,.!?]|$)[\s,.!?]*", "", RegexOptions.IgnoreCase);
    internal async Task<string> ExecuteAsync(string raw, bool fromVoice = false)
    {
        string command = RemoveWakeWord(raw), normalized = Normalize(command);
        if (normalized is "awaryjny stop" or "emergency stop" or "zatrzymaj sentinel" or "zatrzymaj sentinela") { TriggerEmergency(); return "STOP awaryjny aktywny."; }
        if (normalized is "wznow sentinel" or "wznow" or "resume sentinel" or "reset emergency") { emergency = false; StatusText.Text = "Wznowiono. Mikrofon pozostaje wyłączony."; AddMessage("SENTINEL", StatusText.Text); return StatusText.Text; }
        if (normalized is "anuluj" or "przerwij" or "przerwij odpowiedz") { CancelCurrent(); toolbox.CancelPendingAction(); UpdatePermission(); return "Przerwano. Akcje już ukończone nie są cofane."; }
        if (emergency) { string msg = "STOP awaryjny blokuje nowe zadania. Wpisz „wznów Sentinel”."; AddMessage("SENTINEL", msg); return msg; }
        if (string.IsNullOrWhiteSpace(command)) return "Słucham.";
        if (fromVoice && normalized.StartsWith("potwierdz", StringComparison.Ordinal))
        { AddMessage("SENTINEL", "Potwierdź akcję przyciskiem lub wpisz potwierdzenie. Sam głos jej nie zatwierdza."); ShowFromTray(); return "Potwierdź w oknie."; }
        if (busy || installing)
        {
            if (fromVoice && voiceCommands.TryEnqueue(command))
            {
                StatusText.Text = $"Polecenie w kolejce ({voiceCommands.Count}/4): {command}";
                return StatusText.Text;
            }
            StatusText.Text = fromVoice
                ? "Usłyszałem: „" + command + "”, ale trwa poprzednie zadanie. Powtórz po zakończeniu albo powiedz „przerwij”."
                : "Trwa zadanie. Możesz je przerwać przed następnym poleceniem.";
            if (fromVoice) AddMessage("SENTINEL", StatusText.Text);
            return StatusText.Text;
        }
        busy = true; currentTask = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancellationToken token = currentTask.Token; SendButton.IsEnabled = false;
        memory.AddUserMessage(command, fromVoice ? "VOICE" : "TEXT"); AddMessage("TY", command, false); StatusText.Text = "Wykonuję…";
        try
        {
            string response = await BuiltInAsync(command, normalized, token) ?? await RouteAsync(command, token);
            token.ThrowIfCancellationRequested(); AddMessage("SENTINEL", response);
            if (currentPage == "Tools") DiagnosticOutput.Text = response;
            if (currentPage == "Memory") MemoryOutput.Text = response;
            StatusText.Text = memory.LastStorageError ?? "Gotowe.";
            if (fromVoice && settings.Settings.SpeakResponses) Speak(response);
            return response;
        }
        catch (OperationCanceledException) { const string msg = "Przerwano zadanie. Zakończone działania nie są cofane."; AddMessage("SENTINEL", msg); StatusText.Text = msg; return msg; }
        catch (Exception ex) { string msg = "Nie udało się wykonać zadania: " + ex.Message; AddMessage("SENTINEL", msg); StatusText.Text = "Błąd — szczegóły w rozmowie."; AppLog.Write(ex); return msg; }
        finally { busy = false; currentTask?.Dispose(); currentTask = null; SendButton.IsEnabled = true; activeUntil = DateTime.Now.AddMinutes(settings.Settings.ConversationMinutes); UpdatePermission(); DrainVoiceCommands(); }
    }
    private async Task<string> RouteAsync(string command, CancellationToken token)
    {
        var result = await toolbox.ProcessAsync(command, token);
        return result.Handled ? result.Response : await router.ProcessAsync(command, token);
    }
    private async Task<string?> BuiltInAsync(string command, string text, CancellationToken token)
    {
        var build = Regex.Match(text, @"^(?:zbuduj|stworz|utworz) (?:mi )?program (notatnik|kalkulator|pomodoro)$");
        if (build.Success)
        {
            string template = ProgramBuilderService.Templates.First(x => Normalize(x) == build.Groups[1].Value);
            var result = await programBuilder.BuildAsync(template, new Progress<string>(message => { StatusText.Text = message; ProgramOutput.Text = message; }), token);
            string response = $"{result.Status}\n{result.Message}\n{result.Executable}\nProjekt: {result.ProjectDirectory}\nDowód: {result.Evidence}";
            ProgramOutput.Text = response; return response;
        }
        if (!command.Contains(':') || text.Contains("na pulpicie"))
        {
            string? fileResponse = await files.ProcessAsync(command, token);
            if (fileResponse != null) return fileResponse;
        }
        if (TryHandleDotCommand(command, text, out string dotResponse))
            return dotResponse;
        if (TryAnswerLocally(command, text, out string localResponse))
            return localResponse;
        switch (text)
        {
            case "sprawdz siebie": case "self diagnostic": case "..health":
                return await SelfDiagnosticService.RunAsync(settings, ai, voice.GetDiagnostics(), !smokeMode, token);
            case "..tasks": case "zadania": ShowPage("Actions"); return toolbox.GetTaskSummary();
            case "..history": return (await toolbox.ProcessAsync("historia akcji", token)).Response;
            case "nowa rozmowa": case "nowa sesja": case "zacznij nowa rozmowe":
                string sessionResponse = await router.ProcessAsync(command, token); LoadChat(); ShowPage("Chat"); return sessionResponse;
            case "on": case "sluchaj": case "voice on": case "mikrofon on":
                await StartVoiceAsync(); if (voice.IsListening) { voice.SetWakeOnlyMode(false); activeUntil = DateTime.Now.AddMinutes(settings.Settings.ConversationMinutes); } return voice.IsListening ? "Słucham." : lastVoiceStatus;
            case "off": case "of": case "przestan sluchac": case "koniec rozmowy": case "standby": SetStandby(); return voice.IsListening ? "Czuwam. Powiedz „Sentinel”, aby wrócić do rozmowy." : "Mikrofon jest całkowicie wyłączony.";
            case "voice off": case "mikrofon off": case "wylacz mikrofon": case "wylacz mikrofon calkowicie": StopVoice(); return "Mikrofon wyłączony.";
            case "diagnostyka mikrofonu": case "test mikrofonu": return voice.GetDiagnostics();
            case "kalibruj mikrofon": case "kalibracja mikrofonu": if (!voice.IsListening) return "Najpierw włącz mikrofon."; voice.CalibrateNoise(); return "Przez 2 sekundy zachowaj ciszę.";
            case "mikrofony": case "pokaz mikrofony": RefreshDevices(); return string.Join("\n", voice.GetMicrophones());
            case "ustawienia": ShowPage("Settings"); return "Ustawienia.";
            case "historia": ShowPage("Memory"); ShowHistory(); return "Historia rozmów jest w zakładce pamięci.";
            case "system": ShowPage("System"); return HardwareText.Text;
            case "gaming": case "gaming mode": ShowPage("Gaming"); return GamingText.Text;
            case "command": case "command center": ShowPage("Chat"); return "Rozmowa.";
            case "tray on": settings.Settings.CloseToTray = settings.Settings.MinimizeToTray = true; settings.Save(); ApplySettings(); return "Zamykanie i minimalizacja chowają okno do zasobnika.";
            case "tray off": settings.Settings.CloseToTray = settings.Settings.MinimizeToTray = false; settings.Save(); ApplySettings(); return "Zamykanie okna kończy aplikację.";
            case "autostart on": case "wlacz autostart": return SetStartup(true);
            case "autostart off": case "wylacz autostart": return SetStartup(false);
            case "autostart": case "status autostartu": return startup.IsEnabled() ? "Autostart włączony." : "Autostart wyłączony.";
            case "overlay ram": case "pokaz ram w okienku": ShowOverlay("ram"); return Format(monitor.GetUsedRamGB(), " GB", 1);
            case "overlay cpu": case "pokaz cpu w okienku": ShowOverlay("cpu"); return Format(monitor.GetCpuUsage(), "%");
            case "ukryj overlay": case "ukryj nakladke": overlay?.Close(); overlay = null; return "Nakładka ukryta.";
            case "watch on": case "wlacz watch": settings.Settings.WatchEnabled = true; WatchCheck.IsChecked = true; settings.Save(); return "Watch włączony.";
            case "watch off": case "wylacz watch": settings.Settings.WatchEnabled = false; WatchCheck.IsChecked = false; settings.Save(); return "Watch wyłączony.";
            case "diagnoza cs2": case "cs2 doctor": return await DiagnoseGameAsync(token);
            case "pomoc": return BuildHelpText();
        }
        if (text.StartsWith("ustaw mikrofon ") && int.TryParse(text.Split(' ').Last(), out int device))
        { RefreshDevices(); if (device < 0 || device >= MicrophoneCombo.Items.Count) return "Nie ma mikrofonu o takim numerze."; MicrophoneCombo.SelectedIndex = device; return "Wybrano " + MicrophoneCombo.SelectedItem; }
        if (TryHandleSettingsCommand(command, text, out string settingsResponse))
            return settingsResponse;
        var file = Regex.Match(command, @"^(?:utwórz|utworz|napisz|stwórz|stworz)\s+plik\s+([^:\r\n]+):\s*([\s\S]*)$", RegexOptions.IgnoreCase);
        if (file.Success) return await LocalFileService.CreateAsync(file.Groups[1].Value, file.Groups[2].Value, token);
        if (Regex.IsMatch(text, @"^(?:pokaz|podaj|wyswietl) (?:uzycie |zuzycie )?ramu?$"))
        { if (gaming.IsGaming()) ShowOverlay("ram"); return Format(monitor.GetUsedRamGB(), " GB", 1); }
        if (Regex.IsMatch(text, @"^(?:pokaz|podaj|wyswietl) (?:uzycie |zuzycie )?cpu$"))
        { if (gaming.IsGaming()) ShowOverlay("cpu"); return Format(monitor.GetCpuUsage(), "%"); }
        return null;
    }

    private bool TryHandleDotCommand(string command, string text, out string response)
    {
        if (text is "..health" or "..tasks" or "..history") { response = ""; return false; }
        string raw = command.Trim();
        string dot = raw.StartsWith("..", StringComparison.Ordinal) ? raw[2..].Trim().ToLowerInvariant() : "";
        if (dot.Length == 0) { response = ""; return false; }
        dot = ConversationMemoryService.Normalize(dot);
        switch (dot)
        {
            case "help":
            case "h":
            case "?":
            case "pomoc":
                response = BuildHelpText();
                return true;
            case "clear":
            case "cls":
            case "czysc":
            case "wyczysc":
                messages.Clear();
                AddMessage("SENTINEL", "Czat wyczyszczony na ekranie. Historia w pamięci nie została usunięta. Użyj „nowa rozmowa”, jeśli chcesz zacząć nową sesję.", false);
                response = "Czat wyczyszczony.";
                return true;
            case "new":
            case "reset":
            case "nowa":
                _ = Dispatcher.BeginInvoke(new Action(async () => await ExecuteAsync("nowa rozmowa")));
                response = "Tworzę nową rozmowę.";
                return true;
            case "status":
                string model = string.IsNullOrWhiteSpace(ai.LastModel) ? "jeszcze nie użyty" : ai.LastModel;
                response = $"Status: {(busy ? "zajęty" : "gotowy")}. Mikrofon: {lastVoiceStatus}. AI: {model}. CPU {Format(monitor.GetCpuUsage(), "%")}, RAM {Format(monitor.GetRamUsagePercent(), "%")}, GPU {Format(monitor.GetGpuUsagePercent(), "%")}.";
                return true;
            case "settings":
            case "ustawienia":
                response = settings.Summary();
                return true;
            case "voice":
            case "mic":
                response = voice.GetDiagnostics();
                return true;
            case "ai":
            case "models":
            case "modele":
                response = string.IsNullOrWhiteSpace(ModelDetailText.Text) ? "Kliknij „Sprawdź modele lokalne” albo wpisz: modele AI." : ModelDetailText.Text;
                return true;
            case "commands":
            case "komendy":
                response = BuildCommandList();
                return true;
            case "stop":
            case "zatrzymaj":
            case "przerwij":
                CancelCurrent();
                response = "Zatrzymano bieżącą pracę.";
                return true;
            case "rollback":
            case "cofnij aktualizację":
                response = "Rollback konfiguracji nie jest jeszcze wykonywany jako osobna komenda z kropkami.";
                return true;
            default:
                response = "Nie znam tej komendy z kropkami. Wpisz `..help`.";
                return true;
        }
    }

    private bool TryAnswerLocally(string command, string text, out string response)
    {
        if (text is "hej" or "czesc" or "siema" or "witaj")
        {
            response = "Cześć. Jestem gotowy lokalnie: komendy systemowe robię bez AI, trudniejsze pytania kieruję do Ollamy.";
            return true;
        }
        if (text is "co potrafisz" or "co umiesz" or "pomoc" or "help")
        {
            response = BuildHelpText();
            return true;
        }
        if (text is "jaka godzina" or "ktora godzina" or "godzina")
        {
            response = DateTime.Now.ToString("HH:mm:ss");
            return true;
        }
        if (text is "jaka data" or "jaka jest data" or "dzisiejsza data")
        {
            response = DateTime.Now.ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo("pl-PL"));
            return true;
        }
        if (text.Contains("kruki") && text.Contains("zime") && (text.Contains("odlatuja") || text.Contains("dlaczego")))
        {
            response = "Kruki w Polsce zwykle nie odlatują na zimę — kruk zwyczajny jest u nas ptakiem osiadłym. Dorosłe osobniki zostają i znajdują pokarm także zimą, a tylko część młodych rozprasza się na krótkie dystanse. To nie jest klasyczna wędrówka jak u ptaków migrujących.";
            return true;
        }
        if (TryEvaluateSimpleMath(text, out double math))
        {
            response = math.ToString("0.####", CultureInfo.GetCultureInfo("pl-PL"));
            return true;
        }
        response = "";
        return false;
    }

    private bool TryHandleSettingsCommand(string command, string text, out string response)
    {
        response = "";
        if (TryHandleAdvancedSetting(text, out response)) return true;
        Match timeout = Regex.Match(text, @"^(?:ustaw|zmien)\s+(?:timeout|czas)\s+(?:rozmowy\s+)?(?:na\s+)?(\d{1,2})\s*(?:min|minute|minut)?$");
        if (timeout.Success)
        {
            int minutes = Math.Clamp(int.Parse(timeout.Groups[1].Value), 1, 60);
            settings.Settings.Voice.ConversationTimeoutMinutes = minutes;
            settings.Save();
            response = $"Ustawiono timeout rozmowy na {minutes} min. Działa od razu.";
            return true;
        }

        Match ttsVolume = Regex.Match(text, @"^(?:ustaw|zmien)\s+(?:glosnosc|głośność)\s+(?:odpowiedzi|mowy|tts)?\s*(?:na\s+)?(\d{1,3})\s*%?$");
        if (ttsVolume.Success)
        {
            int volume = Math.Clamp(int.Parse(ttsVolume.Groups[1].Value), 0, 100);
            settings.Settings.Voice.SpeechVolume = volume;
            settings.Save();
            response = $"Ustawiono głośność odpowiedzi na {volume}%.";
            return true;
        }

        Match ttsRate = Regex.Match(text, @"^(?:ustaw|zmien)\s+(?:szybkosc|szybkość|tempo)\s+(?:mowy|tts|glosu|głosu)?\s*(?:na\s+)?(-?\d{1,2})$");
        if (ttsRate.Success)
        {
            int rate = Math.Clamp(int.Parse(ttsRate.Groups[1].Value), -5, 5);
            settings.Settings.Voice.SpeechRate = rate;
            settings.Save();
            response = $"Ustawiono tempo mowy na {rate}. Zakres to od -5 do 5.";
            return true;
        }

        Match watchCpu = Regex.Match(text, @"^(?:ustaw|zmien)\s+watch\s+cpu\s+(?:na\s+)?(\d{1,3})\s*%?$");
        if (watchCpu.Success)
        {
            int value = Math.Clamp(int.Parse(watchCpu.Groups[1].Value), 50, 100);
            settings.Settings.Watch.CpuAlertPercent = value;
            settings.Save();
            response = $"Ustawiono próg Watch CPU na {value}%.";
            return true;
        }

        Match watchRam = Regex.Match(text, @"^(?:ustaw|zmien)\s+watch\s+ram\s+(?:na\s+)?(\d{1,3})\s*%?$");
        if (watchRam.Success)
        {
            int value = Math.Clamp(int.Parse(watchRam.Groups[1].Value), 50, 100);
            settings.Settings.Watch.RamAlertPercent = value;
            settings.Save();
            response = $"Ustawiono próg Watch RAM na {value}%.";
            return true;
        }

        if (text is "pokaz ustawienia" or "pokaż ustawienia" or "ustawienia sentinel" or "status ustawien" or "status ustawień")
        {
            response = settings.Summary();
            return true;
        }

        return false;
    }

    private static bool TryEvaluateSimpleMath(string text, out double result)
    {
        result = 0;
        var match = Regex.Match(text.Replace(',', '.'), @"^\s*(-?\d+(?:\.\d+)?)\s*(\+|-|\*|x|/|:)\s*(-?\d+(?:\.\d+)?)\s*$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        double left = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        double right = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        string op = match.Groups[2].Value.ToLowerInvariant();
        if ((op is "/" or ":") && Math.Abs(right) < 0.0000001) return false;
        result = op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" or "x" => left * right,
            "/" or ":" => left / right,
            _ => 0
        };
        return true;
    }

    private string BuildHelpText() =>
        """
        SZYBKIE KOMENDY
        ..help / ..commands / ..clear / ..status / ..voice / ..ai / ..models

        GŁOS
        „Sentinel, włącz Discorda”, „Sentinel, pokaż RAM”, „Sentinel, test internetu”.
        Jeśli usłyszę tekst, status pokaże czy go wysłałem, czy odrzuciłem.

        SYSTEM
        ile używam RAM • pokaż CPU • top procesy • dyski • zabezpieczenia • błędy Windows • eksportuj raport

        APLIKACJE
        włącz Discorda / Steam / Brave / YouTube / CS2 / notatnik / kalkulator
        zamknij Discord — wymaga potwierdzenia w oknie

        AI
        modele AI • model AI auto • ustaw model AI qwen3:1.7b • ustaw model AI qwen3:4b-instruct
        Proste pytania liczę i obsługuję kodem bez Ollamy. Trudniejsze idą do lokalnego modelu.

        USTAWIENIA LIVE
        ustaw timeout rozmowy na 20 min • ustaw głośność odpowiedzi na 70% • ustaw tempo mowy na -1
        ustaw watch cpu 85 • ustaw watch ram 90 • pokaż ustawienia
        """;

    private static string BuildCommandList() =>
        """
        KOMENDY Z KROPKAMI
        ..help        pokaż pomoc
        ..clear       wyczyść widoczny czat
        ..status      status CPU/RAM/GPU/mikrofonu/AI
        ..voice       diagnostyka mikrofonu i ASR
        ..ai          status Ollama i wybranego modelu
        ..models      lista modeli
        ..settings    podsumowanie ustawień
        ..commands    ta lista
        ..tasks       bieżące i ukończone zadania
        ..history     historia akcji i dowody
        ..health      sprawdź moduły Sentinela

        PLIKI I PROGRAMY
        stwórz plik test.txt na pulpicie
        wpisz do niego hello • dopisz do niego kolejny wiersz
        pokaż ten plik
        zbuduj program notatnik / kalkulator / pomodoro
        """;
    private async Task<string> DiagnoseGameAsync(CancellationToken token)
    {
        var samples = new List<float>();
        for (int i = 0; i < 4; i++) { await Task.Delay(500, token); var value = monitor.GetCpuUsage(); if (float.IsFinite(value)) samples.Add(value); }
        double ram = monitor.GetRamUsagePercent(); string game = gaming.GetRunningGame();
        return $"DIAGNOZA GRY • {DateTime.Now:HH:mm:ss}\nGra: {(game.Length > 0 ? game : "nie wykryto")}\nCPU (próbka 2 s): {Format(samples.Count > 0 ? samples.Average() : double.NaN, "%")}\nRAM: {Format(monitor.GetUsedRamGB(), " GB", 1)} / {Format(monitor.GetTotalRamGB(), " GB", 1)}\n\n" +
            (ram >= 90 ? "Pamięć RAM jest blisko zapełnienia. Sprawdź „top procesy”.\n" : double.IsFinite(ram) ? "W tej próbce RAM nie przekracza 90%.\n" : "Brak odczytu RAM.\n") +
            "Te odczyty nie mierzą FPS, frametime ani temperatur. Nie wskazują samodzielnie przyczyny ścinek. Dalsze odczyty: test internetu, błędy Windows, top procesy.\n\nNie zmieniono ustawień gry ani Windows.";
    }
    private void CancelCurrent() { voiceCommands.Clear(); currentTask?.Cancel(); toolbox.CancelAllTasks(); ai.CancelCurrentRequest(); speech.Stop(); StatusText.Text = "Przerywanie…"; }
    private void TriggerEmergency()
    { emergency = true; CancelCurrent(); toolbox.CancelPendingAction(); StopVoice(); StatusText.Text = "STOP awaryjny. Wpisz „wznów Sentinel”, aby odblokować zadania."; AddMessage("SENTINEL", StatusText.Text); UpdatePermission(); }
    private void UpdatePermission() { PermissionPanel.Visibility = toolbox.HasPendingAction ? Visibility.Visible : Visibility.Collapsed; PermissionText.Text = toolbox.PendingSummary; }
    private async Task RefreshAiAsync()
    {
        try
        {
            var models = await ai.GetInstalledModelsAsync(lifetime.Token);
            ModelCombo.ItemsSource = new[] { "auto" }.Concat(models).ToArray(); ModelCombo.SelectedItem = string.IsNullOrEmpty(ai.PreferredModel) ? "auto" : ai.PreferredModel;
            AiStatusText.Text = models.Count > 0 ? $"Ollama lokalnie • {models.Count} modeli" : "Ollama działa • brak lokalnego modelu";
            ModelDetailText.Text = await ai.GetStatusAsync(lifetime.Token);
        }
        catch (OperationCanceledException) { if (!exiting) AiStatusText.Text = "Ollama nie odpowiada."; }
        catch (Exception ex) { AiStatusText.Text = "Ollama niedostępna • komendy i diagnostyka działają"; ModelDetailText.Text = "Uruchom Ollama i pobierz lokalny model, następnie odśwież listę.\n" + ex.Message; }
    }
    private string SetStartup(bool enabled)
    { bool success = enabled ? startup.Enable() : startup.Disable(); settings.Settings.StartWithWindows = startup.IsEnabled(); StartupCheck.IsChecked = settings.Settings.StartWithWindows; settings.Save(); return success ? (enabled ? "Autostart włączony i odczytany z rejestru." : "Autostart wyłączony i zweryfikowany.") : "Nie udało się zmienić autostartu."; }
    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || applyingSettings) return;
        var s = settings.Settings; s.CloseToTray = TrayCheck.IsChecked == true; s.MinimizeToTray = MinimizeCheck.IsChecked == true;
        s.StartMinimized = StartMinimizedCheck.IsChecked == true; s.StartVoiceOnLaunch = StartVoiceCheck.IsChecked == true;
        s.SpeakResponses = SpeakCheck.IsChecked == true; s.WatchEnabled = WatchCheck.IsChecked == true;
        settings.Save(); SettingsStatusText.Text = settings.LastError ?? "Ustawienia zapisane lokalnie.";
    }
    private async void Send_Click(object sender, RoutedEventArgs e) => await SendInputAsync();
    private async Task SendInputAsync() { string text = CommandInput.Text.Trim(); if (text.Length == 0) return; if (!busy && !installing) CommandInput.Clear(); await ExecuteAsync(text); }
    private async void Command_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled = true; await SendInputAsync(); } }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CancelCurrent(); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { CommandInput.Focus(); e.Handled = true; }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.X) { TriggerEmergency(); e.Handled = true; }
    }
    private void Navigate_Click(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);
    private async void Quick_Click(object sender, RoutedEventArgs e) { ShowPage("Chat"); await ExecuteAsync((string)((Button)sender).Tag); }
    private async void Diagnostic_Click(object sender, RoutedEventArgs e) => DiagnosticOutput.Text = await ExecuteAsync((string)((Button)sender).Tag);
    private async void MemoryCommand_Click(object sender, RoutedEventArgs e) => MemoryOutput.Text = await ExecuteAsync((string)((Button)sender).Tag);
    private void Stop_Click(object sender, RoutedEventArgs e) => CancelCurrent();
    private void Emergency_Click(object sender, RoutedEventArgs e) => TriggerEmergency();
    private void Tray_Click(object sender, RoutedEventArgs e) => HideToTray();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();
    private async void RefreshModels_Click(object sender, RoutedEventArgs e) => await RefreshAiAsync();
    private async void ApplyModel_Click(object sender, RoutedEventArgs e) { if (ModelCombo.SelectedItem is string name) { try { ModelDetailText.Text = await ai.SetPreferredModelAsync(name, lifetime.Token); } catch (Exception ex) { ModelDetailText.Text = ex.Message; } } }
    private async void UnloadModel_Click(object sender, RoutedEventArgs e) { if (busy) { ModelDetailText.Text = "Najpierw przerwij odpowiedź AI."; return; } try { if (ai.LastModel.Length > 0) { await ai.UnloadModelAsync(ai.LastModel); ModelDetailText.Text = "Wysłano żądanie zwolnienia modelu: " + ai.LastModel; } else ModelDetailText.Text = "Sentinel nie uruchamiał jeszcze modelu w tej sesji."; } catch (Exception ex) { ModelDetailText.Text = ex.Message; } }
    private void ShowMemory_Click(object sender, RoutedEventArgs e) => MemoryOutput.Text = memory.GetNotesSummary();
    private void ShowHistory_Click(object sender, RoutedEventArgs e) => ShowHistory();
    private void ShowHistory() => MemoryOutput.Text = string.Join("\n\n", memory.GetAllEntries().Select(x => $"{x.Timestamp:dd.MM HH:mm} {(x.Role == "user" ? "TY" : "SENTINEL")}\n{x.Text}"));
    private async void NewSession_Click(object sender, RoutedEventArgs e) => await ExecuteAsync("nowa rozmowa");
    private void Startup_Click(object sender, RoutedEventArgs e) => SettingsStatusText.Text = SetStartup(StartupCheck.IsChecked == true);
    private async void Confirm_Click(object sender, RoutedEventArgs e) => await ExecuteAsync("potwierdź");
    private void CancelPermission_Click(object sender, RoutedEventArgs e) { toolbox.CancelPendingAction(); UpdatePermission(); StatusText.Text = "Anulowano oczekującą akcję."; }
    private async void InspectFile_Click(object sender, RoutedEventArgs e)
    { var dialog = new OpenFileDialog { Title = "Wybierz plik do odczytu metadanych i SHA-256", CheckFileExists = true }; if (dialog.ShowDialog(this) == true) DiagnosticOutput.Text = await ExecuteAsync("analizuj plik " + dialog.FileName); }
}
