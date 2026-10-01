using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Actions;

namespace SentinelX.ViewModels;

/// <summary>0.95 · WARSZTAT — the Tools page behind the „Narzędzia” tab.
/// The catalogue is the single source of truth (ToolCatalog); running an entry goes through the
/// normal engine path, so tools keep their permissions, evidence and history. Nothing is executed
/// automatically: the user picks a tool, sees the exact command and only then runs it.</summary>
public partial class ToolsViewModel : ObservableObject
{
    private readonly IActionEngine engine;
    private readonly IUiDispatcher dispatcher;
    public IReadOnlyList<string> Categories => ToolCatalog.Categories;
    public ObservableCollection<ToolEntry> Results { get; } = [];
    public ObservableCollection<string> Recent { get; } = [];
    [ObservableProperty] private string query = "";
    [ObservableProperty] private string category = ToolCatalog.AllCategories;
    [ObservableProperty] private ToolEntry? selectedTool;
    [ObservableProperty] private string argument = "";
    [ObservableProperty] private string result = "";
    [ObservableProperty] private string status = "Wybierz narzędzie, wpisz argument i uruchom. Wszystko liczy się na tym komputerze.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private string emptyHint = "";
    [ObservableProperty] private string resultSummary = "";

    public ToolsViewModel(IActionEngine engine, IUiDispatcher dispatcher)
    {
        this.engine = engine; this.dispatcher = dispatcher;
        engine.Changed += Sync;
        Filter();
        SelectedTool = Results.FirstOrDefault();
    }

    /// <summary>Exactly what „Uruchom” will send — shown above the button so nothing is a surprise.</summary>
    public string CommandPreview => SelectedTool == null ? "" : ToolCatalog.BuildCommand(SelectedTool, Argument);

    partial void OnQueryChanged(string value) => Filter();
    partial void OnCategoryChanged(string value) => Filter();
    partial void OnArgumentChanged(string value) => OnPropertyChanged(nameof(CommandPreview));

    partial void OnSelectedToolChanged(ToolEntry? value)
    {
        Argument = value is { TakesArgument: true } ? value.Example : "";
        OnPropertyChanged(nameof(CommandPreview));
        Status = value == null ? Status : value.TakesArgument
            ? "Argument możesz zmienić — przykład podpowiada format."
            : "To narzędzie nie potrzebuje argumentu. Uruchom je przyciskiem poniżej.";
    }

    private void Filter()
    {
        Results.Clear();
        foreach (var tool in ToolCatalog.Search(Query, Category)) Results.Add(tool);
        EmptyHint = Results.Count == 0 ? "Nie mam narzędzia o takiej nazwie. Wyczyść szukanie albo wybierz inną kategorię." : "";
        ResultSummary = Results.Count + " z " + ToolCatalog.Entries.Count + " narzędzi w katalogu · wszystko liczy się lokalnie, bez chmury i bez wysyłania danych.";
        if (SelectedTool == null || !Results.Contains(SelectedTool)) SelectedTool = Results.FirstOrDefault();
    }

    [RelayCommand]
    private void Refresh() => Filter();

    [RelayCommand]
    private void ClearQuery()
    {
        Query = ""; Category = ToolCatalog.AllCategories;
    }

    [RelayCommand]
    private void UseExample()
    {
        if (SelectedTool is { TakesArgument: true }) Argument = SelectedTool.Example;
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (SelectedTool == null) { Status = "Najpierw wybierz narzędzie z listy."; return; }
        if (engine.IsStopped) { Status = "STOP awaryjny jest aktywny — wznów Sentinel, żeby uruchomić narzędzie."; return; }
        string command = CommandPreview.Trim().TrimEnd(':').Trim();
        if (SelectedTool.TakesArgument && Argument.Trim().Length == 0)
        {
            Status = "To narzędzie potrzebuje argumentu — wpisz go w polu obok (widzisz przykład).";
            return;
        }
        IsBusy = true;
        Status = "Liczę lokalnie: " + command;
        try
        {
            var outcome = await engine.ExecuteAsync(command);
            Result = outcome.Text;
            HasResult = true;
            Status = "Gotowe: " + SelectedTool.Title + " · lokalnie, bez chmury · wynik możesz skopiować.";
            Recent.Insert(0, SelectedTool.Title + " — " + FirstLine(outcome.Text));
            while (Recent.Count > 8) Recent.RemoveAt(Recent.Count - 1);
        }
        catch (Exception ex)
        {
            Result = "Narzędzie nie odpowiedziało: " + ex.Message;
            HasResult = true;
            Status = "Coś poszło nie tak — szczegóły w wyniku poniżej.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void CopyResult()
    {
        if (Result.Length == 0) { Status = "Nie ma jeszcze wyniku do skopiowania."; return; }
        try { Clipboard.SetText(Result); Status = "Wynik skopiowany do schowka."; }
        catch (Exception ex) { Status = "Nie udało się skopiować (" + ex.GetType().Name + "). Zaznacz tekst ręcznie."; }
    }

    [RelayCommand]
    private void ClearResult()
    {
        Result = ""; HasResult = false; Status = "Wynik wyczyszczony.";
    }

    private void Sync() => dispatcher.Post(() => { }); // state is read on demand; the hook keeps the engine contract

    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\n', '\r']);
        string line = end < 0 ? text : text[..end];
        return line.Length <= 90 ? line : line[..89] + "…";
    }
}
