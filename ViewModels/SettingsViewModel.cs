using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace SentinelX.ViewModels;

public partial class SettingViewModel : ObservableObject
{
    private readonly SettingField field;
    private readonly AppSettingsService store;
    public string Section => field.Section;
    public string Label => field.Label;
    public string Description => field.Description;
    public string[]? Choices => field.Choices;
    public bool IsChoice => Choices != null;
    public bool IsToggle => field.IsToggle;
    public bool IsText => !IsChoice && !IsToggle;
    public bool IsSlider => field.Minimum.HasValue && !field.Integer;
    public double Minimum => field.Minimum ?? 0;
    public double Maximum => field.Maximum ?? 1;
    [ObservableProperty] private double numericValue;
    partial void OnNumericValueChanged(double value) => Value = value.ToString("0.###", CultureInfo.InvariantCulture);
    partial void OnValueChanged(string value)
    {
        if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) NumericValue = number;
    }
    [ObservableProperty] private string value;
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string error = "";
    public SettingViewModel(SettingField field, AppSettingsService store)
    { this.field = field; this.store = store; value = field.Read(); if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) numericValue = number; enabled = bool.TryParse(value, out var b) && b; }
    [RelayCommand] private void Save()
    {
        string before = field.Read();
        Error = field.Write(IsToggle ? Enabled.ToString() : Value) ?? "";
        if (Error.Length != 0) return;
        store.Save();
        if (store.LastError is { } failure) { field.Write(before); Error = failure; }
        else { Value = field.Read(); Error = "Zapisano"; }
    }
}
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService store;
    public ObservableCollection<SettingViewModel> Fields { get; } = [];
    public ICollectionView FilteredFields { get; }
    public string[] Sections { get; } = ["Wygląd", "Głos", "AI", "Watch", "Zasoby", "Ogólne", "Developer"];
    [ObservableProperty] private string selectedSection = "Wygląd";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string status = "Zapisz wybrane ustawienie przyciskiem obok pola. Zmiana działa od razu.";
    public SettingsViewModel(AppSettingsService store)
    {
        this.store = store;
        FilteredFields = CollectionViewSource.GetDefaultView(Fields);
        FilteredFields.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SettingViewModel.Section)));
        FilteredFields.Filter = item => item is SettingViewModel field && $"{field.Section} {field.Label} {field.Description}".Contains(Search, StringComparison.OrdinalIgnoreCase);
        Rebuild(); if (store.LastError != null) Status = store.LastError;
    }
    partial void OnSearchChanged(string value) => FilteredFields.Refresh();
    [RelayCommand] private void ResetSection()
    { store.ResetSection(SelectedSection); Status = store.LastError ?? $"Przywrócono sekcję {SelectedSection}."; Rebuild(); }
    private void Rebuild() { Fields.Clear(); foreach (var field in SettingsCatalog.Create(store)) Fields.Add(new(field, store)); }
}
