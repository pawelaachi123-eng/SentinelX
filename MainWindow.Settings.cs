using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace SentinelX;
public partial class MainWindow
{
    private bool TryHandleAdvancedSetting(string text, out string response)
    {
        response = "";
        var number = Regex.Match(text, @"^(?:ustaw|zmien) (prog vad|opacity overlay|przezroczystosc nakladki|rozmiar nakladki|temperature ai|kontekst ai|koniec wypowiedzi) (?:na |do )?([0-9]+(?:[.,][0-9]+)?)\s*(?:%|ms|tokenow)?$");
        if (number.Success)
        {
            string label = number.Groups[1].Value switch
            {
                "prog vad" => "Próg VAD", "opacity overlay" or "przezroczystosc nakladki" => "Przezroczystość nakładki (%)",
                "rozmiar nakladki" => "Rozmiar nakładki (%)", "temperature ai" => "Temperatura", "kontekst ai" => "Kontekst (tokeny)", _ => "Koniec wypowiedzi (ms)"
            };
            var field = SettingsCatalog.Create(settings).First(x => x.Label == label);
            string? error = field.Write(number.Groups[2].Value);
            if (error != null) { response = error; return true; }
            settings.Save(); response = settings.LastError ?? $"Zapisano: {label} = {field.Read()}. Ustawienie jest aktywne."; return true;
        }
        if (text is "zwieksz czulosc mikrofonu" or "zmniejsz czulosc mikrofonu")
        {
            settings.Settings.Voice.VadThreshold = Math.Clamp(settings.Settings.Voice.VadThreshold + (text.StartsWith("zwieksz") ? -.05 : .05), .2, .85);
            settings.Save(); response = settings.LastError ?? $"Próg VAD: {settings.Settings.Voice.VadThreshold:0.00}. Niższy próg oznacza większą czułość."; return true;
        }
        var model = Regex.Match(text, @"^(?:ustaw|uzywaj) model (?:ai )?(gry|poza grami|lekki|glowny) (?:na )?([\w.:/-]+)$");
        if (model.Success)
        {
            string name = model.Groups[2].Value;
            if (!LocalAiService.IsLocalModelName(name)) { response = "Podaj nazwę lokalnego modelu."; return true; }
            if (model.Groups[1].Value is "gry" or "lekki") settings.Settings.Ai.GamingModel = name; else settings.Settings.Ai.IdleModel = name;
            settings.Save(); response = settings.LastError ?? "Zapisano model: " + name + ". Zostanie użyty przy następnym zapytaniu w trybie auto."; return true;
        }
        return false;
    }
    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        try { if (sender is Button { Tag: string text }) { Clipboard.SetText(text); StatusText.Text = "Skopiowano odpowiedź."; } }
        catch (Exception ex) { StatusText.Text = "Schowek jest chwilowo niedostępny."; AppLog.Write(ex); }
    }
    private void RefreshTasks_Click(object sender, RoutedEventArgs e) => TasksOutput.Text = toolbox.GetTaskSummary();
    private async void BuildProgram_Click(object sender, RoutedEventArgs e) => ProgramOutput.Text = await ExecuteAsync((string)((Button)sender).Tag);
    private async void ActionHistory_Click(object sender, RoutedEventArgs e) => ActionsOutput.Text = await ExecuteAsync((string)((Button)sender).Tag);
}
