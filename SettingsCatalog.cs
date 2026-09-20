using System.Globalization;

namespace SentinelX;

public sealed record SettingField(string Section, string Label, string Description, Func<string> Read, Func<string, string?> Write, string[]? Choices = null, bool IsToggle = false);

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
            }));
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
        Toggle("Ogólne", "Głos przy uruchomieniu", "Wymaga już pobranych modeli i dostępnego mikrofonu.", () => S().Startup.StartVoiceOnLaunch, x => S().Startup.StartVoiceOnLaunch = x);
        Toggle("Głos", "Odpowiedzi głosowe", "Synteza lokalna Windows dla poleceń głosowych.", () => S().Voice.SpeakResponses, x => S().Voice.SpeakResponses = x);
        Toggle("Developer", "Tryb deweloperski", "Diagnostyka; nie daje modelowi zgody na modyfikowanie kodu.", () => S().Developer.DeveloperMode, x => S().Developer.DeveloperMode = x);
        Toggle("Developer", "Zapis próbek audio", "Prywatne nagrania lokalne. Wyłącz domyślnie.", () => S().Voice.SaveVoiceSamples, x => { S().Developer.SaveVoiceSamples = x; S().Voice.SaveVoiceSamples = x; });
        return fields;
    }
}
