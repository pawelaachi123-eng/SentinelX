using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Automation;

namespace SentinelX.ViewModels;

public sealed record AutomationRuleItemViewModel(AutomationRule Rule, string TriggerText, string ActionsText, string LastRunText)
{
    public string Name => Rule.Name;
    public string EnabledText => Rule.Enabled ? "Włączona" : "Wyłączona";
}

public sealed record AutomationHistoryItemViewModel(AutomationRunRecord Run)
{
    public string Detail => $"{Run.FinishedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {Run.Status} · {Run.RuleName} · {Run.CompletedActions}/{Run.TotalActions} akcji · {Run.DurationMilliseconds} ms";
    public string Result => Run.Error.Length > 0 ? Run.Result + " — " + Run.Error : Run.Result;
}

public partial class AutomationActionDraft : ObservableObject
{
    [ObservableProperty] private string actionId;
    [ObservableProperty] private string parameter;
    public AutomationActionDraft(string actionId, string parameter) { this.actionId = actionId; this.parameter = parameter; }
}

/// <summary>Automation editor and execution history; all scheduling and IO remain in AutomationService.</summary>
public partial class AutomationViewModel : ObservableObject, IDisposable
{
    private readonly AutomationService automations;
    private readonly IUiDispatcher dispatcher;
    private string loadedRuleId = "";

    public ObservableCollection<AutomationRuleItemViewModel> Rules { get; } = [];
    public ObservableCollection<AutomationHistoryItemViewModel> History { get; } = [];
    public ObservableCollection<AutomationActionDraft> DraftActions { get; } = [];
    public IReadOnlyList<AutomationTriggerOption> TriggerOptions { get; } =
    [
        new(AutomationTriggerKind.Manual, "Ręcznie", "Uruchamia się wyłącznie po kliknięciu „Uruchom teraz”."),
        new(AutomationTriggerKind.ApplicationStartup, "Przy starcie Sentinel", "Raz po uruchomieniu aplikacji. Działa tylko gdy Sentinel jest uruchomiony."),
        new(AutomationTriggerKind.DailySchedule, "Codziennie o godzinie", "Lokalny czas komputera; po uruchomieniu aplikacji pominięta godzina jest nadrabiana raz tego dnia.")
    ];
    public IReadOnlyList<AutomationActionOption> ActionOptions { get; }

    [ObservableProperty] private AutomationRuleItemViewModel? selectedRule;
    [ObservableProperty] private AutomationTriggerOption? selectedTrigger;
    [ObservableProperty] private AutomationActionOption? selectedAction;
    [ObservableProperty] private string ruleName = "";
    [ObservableProperty] private string scheduleTime = "09:00";
    [ObservableProperty] private bool ruleEnabled = true;
    [ObservableProperty] private string newActionParameter = "";
    [ObservableProperty] private string editorTitle = "Nowa automatyzacja";
    [ObservableProperty] private string status = "Automatyzacje działają tylko wtedy, gdy Sentinel jest uruchomiony.";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isDailySchedule;
    [ObservableProperty] private string storageStatus = "";

    public AutomationViewModel(AutomationService automations, IUiDispatcher dispatcher)
    {
        this.automations = automations;
        this.dispatcher = dispatcher;
        ActionOptions = automations.GetAvailableActions()
            .Select(x => new AutomationActionOption(x.Id, x.Name, x.Description + " · " + x.RequiredPermission)).ToArray();
        SelectedTrigger = TriggerOptions[0];
        SelectedAction = ActionOptions.FirstOrDefault(x => x.Id == "notify") ?? ActionOptions.FirstOrDefault();
        automations.Changed += OnAutomationsChanged;
        Refresh();
        NewRule();
    }

    partial void OnSelectedTriggerChanged(AutomationTriggerOption? value)
    {
        IsDailySchedule = value?.Kind == AutomationTriggerKind.DailySchedule;
    }

    partial void OnSelectedRuleChanged(AutomationRuleItemViewModel? value)
    {
        if (value == null || value.Rule.Id == loadedRuleId) return;
        LoadRule(value);
    }

    private void OnAutomationsChanged() => dispatcher.Post(Refresh);

    [RelayCommand]
    private void Refresh()
    {
        string? selectedId = SelectedRule?.Rule.Id;
        var actionNames = ActionOptions.ToDictionary(x => x.Id, x => x.Label, StringComparer.OrdinalIgnoreCase);
        var loaded = automations.GetRules().Select(rule => new AutomationRuleItemViewModel(
            rule,
            TriggerLabel(rule),
            string.Join(" → ", rule.Actions.Select(action => actionNames.GetValueOrDefault(action.ActionId, action.ActionId))),
            rule.LastRunAt == null ? "Jeszcze nie uruchamiano" : $"Ostatnio: {rule.LastRunAt.Value.ToLocalTime():dd.MM HH:mm} · {rule.LastRunStatus} · {rule.LastRunDurationMilliseconds} ms"))
            .ToArray();
        Rules.Clear(); foreach (var item in loaded) Rules.Add(item);
        SelectedRule = loaded.FirstOrDefault(x => x.Rule.Id == selectedId);
        IsRunning = SelectedRule != null && automations.IsRunning(SelectedRule.Rule.Id);
        var records = automations.GetHistory(100).Select(x => new AutomationHistoryItemViewModel(x)).ToArray();
        History.Clear(); foreach (var record in records) History.Add(record);
        StorageStatus = automations.LastStorageError ?? $"{Rules.Count}/{100} automatyzacji · historia: {History.Count}/500 wpisów · lokalnie na tym komputerze";
    }

    private static string TriggerLabel(AutomationRule rule) => rule.Trigger switch
    {
        AutomationTriggerKind.Manual => "Ręcznie",
        AutomationTriggerKind.ApplicationStartup => "Przy starcie Sentinel",
        AutomationTriggerKind.DailySchedule => "Codziennie · " + rule.ScheduleTime,
        _ => "Nieobsługiwany wyzwalacz"
    };

    private void LoadRule(AutomationRuleItemViewModel item)
    {
        loadedRuleId = item.Rule.Id;
        EditorTitle = "Edytuj automatyzację";
        RuleName = item.Rule.Name;
        SelectedTrigger = TriggerOptions.FirstOrDefault(x => x.Kind == item.Rule.Trigger) ?? TriggerOptions[0];
        ScheduleTime = item.Rule.ScheduleTime.Length > 0 ? item.Rule.ScheduleTime : "09:00";
        RuleEnabled = item.Rule.Enabled;
        DraftActions.Clear();
        foreach (AutomationStep action in item.Rule.Actions)
            DraftActions.Add(new(action.ActionId, action.Parameter));
        NewActionParameter = "";
        Status = "Zmiany zaczną obowiązywać po zapisaniu. Edycja ani usunięcie nie zmieniają historii wykonań.";
    }

    [RelayCommand]
    private void NewRule()
    {
        loadedRuleId = "";
        SelectedRule = null;
        EditorTitle = "Nowa automatyzacja";
        RuleName = "";
        SelectedTrigger = TriggerOptions[0];
        ScheduleTime = "09:00";
        RuleEnabled = true;
        DraftActions.Clear();
        NewActionParameter = "";
        SelectedAction ??= ActionOptions.FirstOrDefault(x => x.Id == "notify") ?? ActionOptions.FirstOrDefault();
        Status = "Wybierz nazwę, wyzwalacz i co najmniej jedną akcję. Brak powłoki systemowej i nieograniczonych poleceń.";
    }

    [RelayCommand]
    private void AddAction()
    {
        if (DraftActions.Count >= 5) { Status = "Limit to pięć kolejnych akcji w jednej automatyzacji."; return; }
        if (SelectedAction == null) { Status = "Wybierz typ akcji."; return; }
        var step = new AutomationStep(SelectedAction.Id, NewActionParameter);
        if (!automations.TryValidateAction(step, out AutomationStep normalized, out string error))
        { Status = error; return; }
        DraftActions.Add(new(normalized.ActionId, normalized.Parameter));
        NewActionParameter = "";
        Status = $"Dodano akcję: {SelectedAction.Label}. Kolejność na liście jest kolejnością wykonania.";
    }

    [RelayCommand]
    private void RemoveAction(AutomationActionDraft? action)
    {
        if (action == null) return;
        DraftActions.Remove(action);
        Status = "Usunięto akcję z bieżącego szkicu. Zmiana nie jest zapisana do czasu kliknięcia „Zapisz”.";
    }

    [RelayCommand]
    private void SaveRule()
    {
        AutomationTriggerKind trigger = SelectedTrigger?.Kind ?? AutomationTriggerKind.Manual;
        var candidate = new AutomationRule
        {
            Id = loadedRuleId,
            Name = RuleName,
            Trigger = trigger,
            ScheduleTime = IsDailySchedule ? ScheduleTime : "",
            Enabled = RuleEnabled,
            Actions = DraftActions.Select(x => new AutomationStep(x.ActionId, x.Parameter)).ToArray()
        };
        AutomationRule? saved = automations.SaveRule(candidate, out string error);
        if (saved == null) { Status = error; Refresh(); return; }
        loadedRuleId = saved.Id;
        Status = $"Zapisano „{saved.Name}” · {saved.Actions.Count} akcji · {TriggerLabel(saved)}. Uruchamianie wymaga, że Sentinel pozostaje otwarty lub działa w zasobniku.";
        Refresh();
        SelectedRule = Rules.FirstOrDefault(x => x.Rule.Id == saved.Id);
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedRule == null) { Status = "Wybierz automatyzację z listy."; return; }
        LoadRule(SelectedRule);
    }

    [RelayCommand]
    private async Task RunSelectedAsync(CancellationToken token)
    {
        if (SelectedRule == null) { Status = "Wybierz automatyzację do uruchomienia."; return; }
        string id = SelectedRule.Rule.Id;
        IsRunning = true;
        Status = "Uruchamiam kolejno zatwierdzone akcje…";
        try
        {
            AutomationRunOutcome outcome = await automations.RunNowAsync(id, token);
            Status = outcome.Message + (outcome.Record == null ? "" : $" · {outcome.Record.Status} · {outcome.Record.DurationMilliseconds} ms");
        }
        catch (OperationCanceledException) { Status = "Anulowano oczekiwanie. Wykonane wcześniej akcje nie są cofane."; }
        catch (Exception ex) { Status = "Nie udało się uruchomić automatyzacji. " + ex.Message; AppLog.Write("Automation", "Error", "Manual automation execution failed.", ex); }
        finally { IsRunning = false; Refresh(); }
    }

    [RelayCommand]
    private void CancelSelected()
    {
        if (SelectedRule == null) { Status = "Wybierz działającą automatyzację."; return; }
        if (!automations.Cancel(SelectedRule.Rule.Id))
        { Status = "Ta automatyzacja nie jest uruchomiona albo zakończyła się już działaniem."; return; }
        Status = "Wysłano prośbę o anulowanie. Wcześniej wykonane akcje nie są cofane.";
    }

    [RelayCommand]
    private void ToggleEnabled()
    {
        if (SelectedRule == null) { Status = "Wybierz automatyzację."; return; }
        bool enable = !SelectedRule.Rule.Enabled;
        if (!automations.SetEnabled(SelectedRule.Rule.Id, enable, out string error)) { Status = error; return; }
        Status = enable ? "Włączono wyzwalacz automatyzacji." : "Wyłączono wyzwalacz. Bieżące wykonanie nie zostało przerwane.";
        Refresh();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedRule == null) { Status = "Wybierz automatyzację do usunięcia."; return; }
        string name = SelectedRule.Rule.Name;
        if (!automations.Delete(SelectedRule.Rule.Id, out string error)) { Status = error; return; }
        NewRule();
        Status = $"Usunięto konfigurację „{name}”. Wcześniejsza historia wykonań została zachowana.";
        Refresh();
    }

    public void Dispose() => automations.Changed -= OnAutomationsChanged;
}
