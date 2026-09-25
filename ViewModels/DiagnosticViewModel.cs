using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;

namespace SentinelX.ViewModels;

public sealed class SnapshotItemViewModel
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public override string ToString() => Detail;
}

/// <summary>Diagnostic snapshots page state: capture, compare two readings, export, delete. No IO here — the service owns it.</summary>
public partial class DiagnosticViewModel : ObservableObject, IDisposable
{
    private readonly DiagnosticSnapshotService snapshots;
    private readonly IUiDispatcher dispatcher;

    public ObservableCollection<SnapshotItemViewModel> Items { get; } = [];
    [ObservableProperty] private SnapshotItemViewModel? firstSnapshot;
    [ObservableProperty] private SnapshotItemViewModel? secondSnapshot;
    [ObservableProperty] private string label = "";
    [ObservableProperty] private string status = "Odczyt diagnostyczny to obraz stanu w jednej chwili — nie diagnoza kondycji sprzętu.";
    [ObservableProperty] private string comparison = "";
    [ObservableProperty] private bool canCompare;
    [ObservableProperty] private bool hasComparison;

    public DiagnosticViewModel(DiagnosticSnapshotService snapshots, IUiDispatcher dispatcher)
    {
        this.snapshots = snapshots; this.dispatcher = dispatcher;
        snapshots.Changed += Sync;
        Refresh();
    }

    private void Sync() => dispatcher.Post(Refresh);

    [RelayCommand]
    public void Refresh()
    {
        Items.Clear();
        var stored = snapshots.GetSnapshots().OrderBy(x => x.CapturedAt).ToArray();
        for (int i = 0; i < stored.Length; i++)
            Items.Add(new SnapshotItemViewModel
            {
                Id = stored[i].Id,
                Label = stored[i].Label,
                Detail = $"{i + 1}. {stored[i].Label} · {stored[i].CapturedAt:dd.MM.yyyy HH:mm:ss} · {stored[i].Sections.Count} sekcji"
            });
        FirstSnapshot = Items.FirstOrDefault(x => x.Id == FirstSnapshot?.Id);
        SecondSnapshot = Items.FirstOrDefault(x => x.Id == SecondSnapshot?.Id);
        UpdateCanCompare();
        if (snapshots.LastStorageError is { Length: > 0 } warning) Status = warning;
    }

    partial void OnFirstSnapshotChanged(SnapshotItemViewModel? value) => UpdateCanCompare();
    partial void OnSecondSnapshotChanged(SnapshotItemViewModel? value) => UpdateCanCompare();

    private void UpdateCanCompare() => CanCompare = FirstSnapshot != null && SecondSnapshot != null && FirstSnapshot.Id != SecondSnapshot.Id;

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CaptureAsync(CancellationToken token)
    {
        Status = "Zbieram odczyt diagnostyczny (dyski, usługi, zdarzenia, sieć)…";
        try
        {
            var captured = await snapshots.CaptureAsync(Label, token);
            Label = "";
            Status = captured == null
                ? "Nie zapisano odczytu. " + (snapshots.LastStorageError ?? "")
                : $"Zapisano: {captured.Label} · {captured.CapturedAt:dd.MM.yyyy HH:mm:ss} · {captured.Sections.Count} sekcji. Porównaj dwa odczyty, żeby zobaczyć różnice.";
        }
        catch (OperationCanceledException) { Status = "Przerwano zbieranie odczytu. Nic nie zostało zapisane."; }
    }

    [RelayCommand]
    private void Compare()
    {
        if (FirstSnapshot == null || SecondSnapshot == null) { Status = "Wybierz dwa różne odczyty do porównania."; return; }
        var diff = snapshots.Compare(FirstSnapshot.Id, SecondSnapshot.Id, out string reason);
        if (diff == null) { Comparison = ""; HasComparison = false; Status = reason; return; }
        Comparison = diff.ToMarkdown();
        HasComparison = true;
        Status = diff.Headline + " Różnica pokazuje tylko, co się zmieniło — nie wyjaśnia przyczyny.";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (FirstSnapshot == null || SecondSnapshot == null) { Status = "Najpierw wybierz dwa odczyty i porównaj je."; return; }
        Status = "Zapisuję porównanie lokalnie…";
        var result = await snapshots.ExportComparisonAsync(FirstSnapshot.Id, SecondSnapshot.Id);
        Status = result.Status switch
        {
            "VERIFIED" => "Zapisano i sprawdzono odczytem zwrotnym. Plik został tylko na tym komputerze.\n" + result.Evidence,
            "UNVERIFIED" => "Zapisano bez pełnego dowodu.\n" + result.Message + "\n" + result.Evidence,
            _ => "Nie zapisano porównania. " + result.Message + (result.Evidence.Length > 0 ? "\n" + result.Evidence : "")
        };
    }

    [RelayCommand]
    private void Delete(SnapshotItemViewModel? item)
    {
        if (item == null) { Status = "Wybierz odczyt do usunięcia."; return; }
        Status = snapshots.Delete(item.Id)
            ? $"Usunięto wyłącznie odczyt {item.Label}. Pozostałe odczyty i wcześniej zapisane raporty są nietknięte."
            : "Nie usunięto odczytu. " + (snapshots.LastStorageError ?? "");
    }

    public void Dispose() => snapshots.Changed -= Sync;
}
