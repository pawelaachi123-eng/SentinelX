using System.Globalization;

namespace SentinelX;

public sealed record SettingField(string Section, string Label, string Description, Func<string> Read, Func<string, string?> Write, string[]? Choices = null, bool IsToggle = false, double? Minimum = null, double? Maximum = null, bool Integer = false);

public static class SettingsCatalog
{
    public static IReadOnlyList<SettingField> Create(AppSettingsService store)
    {
        SentinelSettings S() => store.Settings;
        var fields = new List<SettingField>();
        void Number(string section, string label, string description, Func<double> read, Action<double> write, double min, double max, bool integer = false)
        {
            fields.Add(new(section, label, description, () => read().ToString("0.###", CultureInfo.InvariantCulture), text =>
            {
                if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < min || value > max || integer && value != Math.Truncate(value))
                    return $"Wpisz {(integer ? "liczbę całkowitą " : "liczbę ")}od {min} do {max}.";
                write(value); return null;
            }, Minimum: min, Maximum: max, Integer: integer));
        }
        void Choice(string section, string label, string description, Func<string> read, Action<string> write, params string[] choices) =>
            fields.Add(new(section, label, description, read, value => { if (!choices.Contains(value)) return "Wybierz wartość z listy."; write(value); return null; }, choices));
        void Toggle(string section, string label, string description, Func<bool> read, Action<bool> write) =>
            fields.Add(new(section, label, description, () => read().ToString(), value => { if (!bool.TryParse(value, out bool enabled)) return "Niepoprawna wartość."; write(enabled); return null; }, IsToggle: true));
        void Model(string label, string description, Func<string> read, Action<string> write) =>
            fields.Add(new("AI", label, description, read, value => { if (!LocalAiService.IsLocalModelName(value)) return "Podaj nazwę lokalnego modelu Ollama."; write(value.Trim()); return null; }));

        Choice("Wygląd", "Motyw", "Zmiana kolorów całego interfejsu od razu.", () => S().Ui.Theme, x => S().Ui.Theme = x, "Dark", "Deep Dark", "System");
        Choice("Wygląd", "Przeglądarka", "Strony i wyszukiwanie. Gdy wybranej nie ma, używana jest domyślna przeglądarka Windows.", () => S().Ui.DefaultBrowserPreference, x => S().Ui.DefaultBrowserPreference = x, "Brave", "Chrome", "System");
        Choice("Wygląd", "Kolor akcentu", "Mięta, błękit, fiolet, bursztyn albo róż.", () => S().Ui.AccentColor, x => S().Ui.AccentColor = x, "#00D4FF", "#66F2C2", "#48D8FF", "#A98BFF", "#FFC86B", "#FF8DA6");
        Number("Wygląd", "Przezroczystość nakładki (%)", "Stosowana również do otwartej nakładki.", () => S().Ui.OverlayOpacityPercent, x => S().Ui.OverlayOpacityPercent = (int)x, 20, 100, true);
        Number("Wygląd", "Rozmiar nakładki (%)", "Skalowanie tekstu i karty.", () => S().Ui.OverlayScalePercent, x => S().Ui.OverlayScalePercent = (int)x, 60, 180, true);
        Choice("Wygląd", "Pozycja nakładki", "Róg głównego ekranu; kartę można też przeciągać.", () => S().Ui.OverlayPosition, x => S().Ui.OverlayPosition = x, "Lewy górny", "Prawy górny", "Lewy dolny", "Prawy dolny");
        Number("Głos", "Timeout rozmowy (min)", "Po bezczynności wraca do czuwania STANDBY.", () => S().Voice.ConversationTimeoutMinutes, x => S().Voice.ConversationTimeoutMinutes = (int)x, 1, 60, true);
        Number("Głos", "Koniec wypowiedzi (ms)", "Długość ciszy przed przekazaniem zdania do rozpoznawania.", () => S().Voice.EndOfSpeechMilliseconds, x => S().Voice.EndOfSpeechMilliseconds = (int)x, 250, 1800, true);
        Number("Głos", "Próg VAD", "Niższa wartość zwiększa czułość detektora mowy.", () => S().Voice.VadThreshold, x => S().Voice.VadThreshold = x, .2, .85);
        Number("Głos", "Minimalna mowa (ms)", "Bardzo krótkie dźwięki nie tworzą poleceń.", () => S().Voice.MinimumSpeechMilliseconds, x => S().Voice.MinimumSpeechMilliseconds = (int)x, 100, 1000, true);
        Number("Głos", "Maksymalna wypowiedź (s)", "Zabezpiecza kolejkę przed niekończącym się nagraniem.", () => S().Voice.MaximumSpeechSeconds, x => S().Voice.MaximumSpeechSeconds = (int)x, 5, 45, true);
        Number("Głos", "Limit wzmocnienia mikrofonu", "Maksymalny mnożnik AGC. Surowy miernik pozostaje niezależny.", () => S().Voice.MaximumGain, x => S().Voice.MaximumGain = x, 1, 12);
        Number("Głos", "Docelowy poziom RMS", "Wzmocnienie jest dopasowane do wykrytej mowy.", () => S().Voice.MicGainTargetRms, x => S().Voice.MicGainTargetRms = x, .02, .2);
        Number("Głos", "Bramka szumu", "Mnożnik zmierzonego szumu tła.", () => S().Voice.NoiseGateMultiplier, x => S().Voice.NoiseGateMultiplier = x, 1, 6);
        Choice("Głos", "Rozpoznawanie wybudzenia", "Strict: dokładne Sentinel; Balanced: typowe warianty ASR.", () => S().Voice.WakeWordMode, x => S().Voice.WakeWordMode = x, "Strict", "Balanced", "Forgiving");
        Number("Głos", "Głośność odpowiedzi (%)", "Głośność lokalnej syntezy Windows.", () => S().Voice.SpeechVolume, x => S().Voice.SpeechVolume = (int)x, 0, 100, true);
        Number("Głos", "Tempo odpowiedzi", "Od -5 (wolno) do 5 (szybko).", () => S().Voice.SpeechRate, x => S().Voice.SpeechRate = (int)x, -5, 5, true);
        Toggle("Głos", "Log diagnostyczny wypowiedzi", "Czasy, poziomy sygnału i powody odrzucenia; bez zapisu audio.", () => S().Voice.UtteranceLoggingEnabled, x => S().Voice.UtteranceLoggingEnabled = x);
        Toggle("Głos", "Zapisuj transkrypcje w logu", "Opcjonalny lokalny zapis rozpoznanych słów.", () => S().Voice.IncludeTranscriptInLogs, x => S().Voice.IncludeTranscriptInLogs = x);
        Model("Model podczas gry", "Używany przy wykrytej grze lub dużym obciążeniu.", () => S().Ai.GamingModel, x => S().Ai.GamingModel = x);
        Model("Model przy wolnych zasobach", "Model rozmowy w trybie automatycznym.", () => S().Ai.IdleModel, x => S().Ai.IdleModel = x);
        Model("Model awaryjny", "Używany, gdy preferowany lokalny model nie odpowiada.", () => S().Ai.FallbackModel, x => S().Ai.FallbackModel = x);
        Number("AI", "Temperatura", "Niższa wartość daje bardziej przewidywalne odpowiedzi.", () => S().Ai.Temperature, x => S().Ai.Temperature = x, 0, 1.5);
        Number("AI", "Kontekst (tokeny)", "Większy kontekst zużywa więcej RAM i VRAM.", () => S().Ai.MaxContextTokens, x => S().Ai.MaxContextTokens = (int)x, 1024, 32768, true);
        Number("AI", "Limit odpowiedzi (tokeny)", "Maksymalna długość jednej odpowiedzi AI.", () => S().Ai.MaxResponseTokens, x => S().Ai.MaxResponseTokens = (int)x, 128, 4096, true);
        Number("AI", "Próg obciążenia RAM (%)", "Powyżej progu wybierany jest model lekki.", () => S().Ai.RamPressurePercent, x => S().Ai.RamPressurePercent = (int)x, 50, 98, true);
        Number("AI", "Próg obciążenia CPU (%)", "Powyżej progu wybierany jest model lekki.", () => S().Ai.CpuPressurePercent, x => S().Ai.CpuPressurePercent = (int)x, 50, 99, true);
        Number("AI", "Próg obciążenia GPU (%)", "Niedostępny odczyt nie jest traktowany jako zerowe użycie.", () => S().Ai.GpuPressurePercent, x => S().Ai.GpuPressurePercent = (int)x, 30, 99, true);
        Toggle("Pamięć", "Zapisuj rozmowy", "Gdy wyłączone, polecenia i odpowiedzi nie zostawiają trwałego śladu w rozmowach.", () => S().Memory.SaveConversations, x => S().Memory.SaveConversations = x);
        Toggle("Pamięć", "AI używa historii rozmów", "Dopowiedzenia czytają ostatnie wypowiedzi. Niezależne od zapisu rozmów.", () => S().Memory.UseHistoryForAi, x => S().Memory.UseHistoryForAi = x);
        Toggle("Pamięć", "Zapisuj wspomnienia", "Polecenie „zapamiętaj” i panel Pamięci dodają trwałe wpisy. Wyłączenie nie kasuje istniejących.", () => S().Memory.SaveMemories, x => S().Memory.SaveMemories = x);
        Toggle("Pamięć", "AI używa wspomnień", "Profil i trwałe wspomnienia trafiają do kontekstu modelu. Niezależne od zapisu wspomnień.", () => S().Memory.UseMemoriesForAi, x => S().Memory.UseMemoriesForAi = x);
        Number("Pamięć", "Retencja rozmów (dni)", "Po ilu dniach historia rozmów jest usuwana. 0 = bezterminowo. Wspomnienia nie są kasowane automatycznie.", () => S().Memory.RetentionDays, x => S().Memory.RetentionDays = (int)x, 0, 3650, true);
        Toggle("Pamięć", "Podgląd kontekstu AI", "Rejestruje, które wspomnienia i fragmenty rozmowy trafiły do modelu (etykiety i powody, nie pełne prompty).", () => S().Memory.ContextPreviewEnabled, x => S().Memory.ContextPreviewEnabled = x);
        Number("Pamięć", "Archiwum rozmów (miesiące)", "Po ilu miesiącach rozmowy trafiają do Memory/Archives (Markdown + JSON z hashem) i są usuwane z aktywnego magazynu. 0 = bez archiwizacji. Wspomnienia nie są archiwizowane.", () => S().Memory.ArchiveMonths, x => S().Memory.ArchiveMonths = (int)x, 0, 120, true);
        Toggle("Watch", "Monitoruj obciążenie", "Powiadomienie po przekroczeniu progu przez zadany czas.", () => S().Watch.Enabled, x => S().Watch.Enabled = x);
        Number("Watch", "Alarm CPU (%)", "Alert przy długotrwałym obciążeniu.", () => S().Watch.CpuAlertPercent, x => S().Watch.CpuAlertPercent = (int)x, 50, 100, true);
        Number("Watch", "Alarm RAM (%)", "Alert przy długotrwałym zapełnieniu pamięci.", () => S().Watch.RamAlertPercent, x => S().Watch.RamAlertPercent = (int)x, 50, 100, true);
        Number("Watch", "Czas do alarmu (s)", "Krótkie skoki obciążenia nie wywołują alertu.", () => S().Watch.MinSecondsBeforeAlert, x => S().Watch.MinSecondsBeforeAlert = (int)x, 3, 300, true);
        Number("Watch", "Przerwa między alarmami (min)", "Ograniczenie powtarzających się powiadomień.", () => S().Watch.CooldownMinutes, x => S().Watch.CooldownMinutes = (int)x, 1, 120, true);
        Number("Zasoby", "Odświeżanie systemu (s)", "Rzadziej oznacza mniejszy koszt monitorowania.", () => S().Resources.MonitorIntervalSeconds, x => S().Resources.MonitorIntervalSeconds = (int)x, 1, 10, true);
        Number("Zasoby", "Odświeżanie podczas gry (s)", "Automatycznie po wykryciu gry.", () => S().Resources.GamingMonitorIntervalSeconds, x => S().Resources.GamingMonitorIntervalSeconds = (int)x, 2, 15, true);
        Toggle("Wygląd", "Animacje", "Wyłączane także automatycznie podczas gry.", () => S().Ui.AnimationsEnabled, x => S().Ui.AnimationsEnabled = x);
        Toggle("Ogólne", "Zamknij do zasobnika", "Przycisk X chowa okno, Wyjdź w zasobniku kończy aplikację.", () => S().Ui.CloseToTray, x => S().Ui.CloseToTray = x);
        Toggle("Ogólne", "Start z Windows", "Autostart tylko dla bieżącego użytkownika.", () => S().Startup.StartWithWindows, x => S().Startup.StartWithWindows = x);
        Toggle("Ogólne", "Uruchom zminimalizowany", "Przy starcie schowaj okno do zasobnika.", () => S().Startup.StartMinimized, x => S().Startup.StartMinimized = x);
        Toggle("Ogólne", "Głos przy uruchomieniu", "Domyślnie WŁĄCZONE (0.91): Sentinel nasłuchuje od startu — wskaźnik 🎤 w Centrum pokazuje stan, jeden klik wyłącza. Wymaga już pobranych modeli i dostępnego mikrofonu.", () => S().Startup.StartVoiceOnLaunch, x => S().Startup.StartVoiceOnLaunch = x);
        Toggle("Głos", "Odpowiedzi głosowe", "Synteza lokalna Windows dla poleceń głosowych.", () => S().Voice.SpeakResponses, x => S().Voice.SpeakResponses = x);
        Toggle("Developer", "Tryb deweloperski", "Diagnostyka; nie daje modelowi zgody na modyfikowanie kodu.", () => S().Developer.DeveloperMode, x => S().Developer.DeveloperMode = x);
        Toggle("Developer", "Zapis próbek audio", "Prywatne nagrania lokalne. Wyłącz domyślnie.", () => S().Voice.SaveVoiceSamples, x => { S().Developer.SaveVoiceSamples = x; S().Voice.SaveVoiceSamples = x; });
        // ---- 0.96 · warstwa JARVIS: pogoda, multimedia, dom, agent, wizja, indeks, sekwencje ----
        void Text(string section, string label, string description, Func<string> read, Action<string> write, int maxLength) =>
            fields.Add(new(section, label, description, read, value =>
            {
                string trimmed = (value ?? "").Trim();
                if (trimmed.Length > maxLength) return $"Najwyżej {maxLength} znaków.";
                write(trimmed); return null;
            }));
        Text("Jarvis", "Miasto dla pogody", "Używane, gdy polecenie „pogoda” nie podaje miasta. Zapisywane lokalnie; miasto jest wysyłane wyłącznie do Open-Meteo przy pobieraniu prognozy.",
            () => S().Jarvis.DefaultCity, x => S().Jarvis.DefaultCity = x, 64);
        Toggle("Jarvis", "Pobieranie prognozy", "JEDYNE polecenie w tym produkcie, które łączy się z internetem (Open-Meteo, bez konta i bez klucza). Wyłączone = Sentinel nie dzwoni na zewnątrz w ogóle.",
            () => S().Jarvis.WeatherEnabled, x => S().Jarvis.WeatherEnabled = x);
        Number("Jarvis", "Ważność prognozy (min)", "Dłużej = mniej żądań. Po tym czasie wynik jest odświeżany; przy braku sieci pokazujemy zapis z adnotacją, że jest starszy.",
            () => S().Jarvis.WeatherCacheMinutes, x => S().Jarvis.WeatherCacheMinutes = (int)x, 5, 720, true);
        Toggle("Jarvis", "Tryb agenta", "Model lokalny może prosić o wykonanie narzędzi WYŁĄCZNIE do odczytu (20 z katalogu), z limitem kroków i transkrypcją. Zgody „potwierdź” agent nie ma.",
            () => S().Jarvis.AgentEnabled, x => S().Jarvis.AgentEnabled = x);
        Number("Jarvis", "Limit kroków agenta", "Ile wywołań narzędzi na jedno zadanie. 1 = model odpowiada bez szukania.",
            () => S().Jarvis.AgentMaxSteps, x => S().Jarvis.AgentMaxSteps = (int)x, 1, 8, true);
        fields.Add(new("Jarvis", "Model narzędzi", "Pusto = bieżący model rozmowy. Musi to być model z obsługą function calling (np. qwen3:4b, llama3.1:8b).",
            () => S().Jarvis.AgentModel, value => { var v = (value ?? "").Trim(); if (v.Length > 0 && !LocalAiService.IsLocalModelName(v)) return "Podaj nazwę lokalnego modelu Ollama albo zostaw puste."; S().Jarvis.AgentModel = v; return null; }));
        fields.Add(new("Jarvis", "Model wizyjny", "Pusto = szukany po nazwie wśród zainstalowanych (llava, qwen2.5vl, minicpm, gemma3…). „co jest na ekranie” bez takiego modelu odmawia.",
            () => S().Jarvis.VisionModel, value => { var v = (value ?? "").Trim(); if (v.Length > 0 && !LocalAiService.IsLocalModelName(v)) return "Podaj nazwę lokalnego modelu Ollama albo zostaw puste."; S().Jarvis.VisionModel = v; return null; }));
        fields.Add(new("Jarvis", "Model osadzeń", "Do liczenia indeksu semantycznego, np. nomic-embed-text. Używany tylko lokalnie, przez Ollamę.",
            () => S().Jarvis.EmbeddingModel, value => { var v = (value ?? "").Trim(); if (v.Length == 0) return "Podaj nazwę lokalnego modelu osadzeń."; if (!LocalAiService.IsLocalModelName(v)) return "Podaj nazwę lokalnego modelu Ollama."; S().Jarvis.EmbeddingModel = v; return null; }));
        Toggle("Jarvis", "Indeks semantyczny", "Opcjonalny dodatek do wyszukiwania tekstowego (wektory w Memory/semantic-index.json). Wyszukiwanie „szukaj wszystkiego” zostaje bez zmian.",
            () => S().Jarvis.SemanticSearchEnabled, x => S().Jarvis.SemanticSearchEnabled = x);
        Toggle("Jarvis", "Integracja z domem", "Home Assistant w sieci lokalnej, bez chmury. Wyłączone dopóki nie włączysz — bo steruje fizycznymi urządzeniami.",
            () => S().Jarvis.HomeEnabled, x => S().Jarvis.HomeEnabled = x);
        Text("Jarvis", "Adres Home Assistanta", "Tylko http(s)://host:port w sieci lokalnej, bez ścieżki i bez logowania w adresie. Tokena NIE zapisujemy: ustaw zmienną środowiskową SENTINEL_HA_TOKEN.",
            () => S().Jarvis.HomeBaseUrl, x => S().Jarvis.HomeBaseUrl = x, 160);
        Choice("Wygląd", "Tło okna", "Mica lub Akryl to efekt Windows 11 (przeszklenie pulpitu za oknem). Na Windows 10 system go nie udostępnia i zostanie zwykłe tło z tokenów.",
            () => S().Ui.WindowBackdrop, x => S().Ui.WindowBackdrop = x, "Mica", "Akryl", "Brak");
        Toggle("Wygląd", "Ciemny pasek tytułu", "Pasek tytułu w kolorze motywu (DWM), żeby okno nie miało jasnej „czapki” w ciemnym motywie.",
            () => S().Ui.DarkTitleBar, x => S().Ui.DarkTitleBar = x);
        return fields;
    }
}
