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
    public string Section => this.field.Section;
    public string Label => this.field.Label;
    public string Description => this.field.Description;
    public string[]? Choices => this.field.Choices;
    public bool IsChoice => Choices != null;
    public bool IsToggle => this.field.IsToggle;
    public bool IsText => !IsChoice && !IsToggle;
    public bool IsSlider => this.field.Minimum.HasValue && !this.field.Integer;
    public double Minimum => this.field.Minimum ?? 0;
    public double Maximum => this.field.Maximum ?? 1;
    [ObservableProperty] private double numericValue;
    partial void OnNumericValueChanged(double value) => Value = value.ToString("0.###", CultureInfo.InvariantCulture);
    partial void OnValueChanged(string value)
    {
        IsSaved = false;
        Error = "";
        if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) NumericValue = number;
    }
    partial void OnEnabledChanged(bool value) { IsSaved = false; Error = ""; }
    [ObservableProperty] private string value;
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool isSaved;
    public SettingViewModel(SettingField field, AppSettingsService store)
    { this.field = field; this.store = store; value = field.Read(); if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) numericValue = number; enabled = bool.TryParse(value, out var b) && b; }
    [RelayCommand] private void Save()
    {
        IsSaved = false;
        string before = field.Read();
        Error = field.Write(IsToggle ? Enabled.ToString() : Value) ?? "";
        if (Error.Length != 0) return;
        store.Save();
        if (store.LastError is { } failure) { field.Write(before); Error = failure; }
        else { Value = field.Read(); IsSaved = true; }
    }
}
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService store;
    public ObservableCollection<SettingViewModel> Fields { get; } = [];
    public ICollectionView FilteredFields { get; }
    public string[] Sections { get; } = ["Wszystkie", "Wygląd", "Głos", "AI", "Telefon", "Pamięć", "Watch", "Zasoby", "Ogólne", "Developer"];
    [ObservableProperty] private string selectedSection = "Wszystkie";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string status = "Zapisz wybrane ustawienie przyciskiem obok pola. Zmiana działa od razu.";
    [ObservableProperty] private bool isResetConfirmationOpen;
    [ObservableProperty] private string resetConfirmationText = "";
    public SettingsViewModel(AppSettingsService store)
    {
        this.store = store;
        FilteredFields = CollectionViewSource.GetDefaultView(Fields);
        FilteredFields.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SettingViewModel.Section)));
        FilteredFields.Filter = item => item is SettingViewModel field
            && (SelectedSection == "Wszystkie" || field.Section == SelectedSection)
            && Matches(field);
        Rebuild(); if (store.LastError != null) Status = store.LastError;
    }
    private bool Matches(SettingViewModel field)
    {
        string query = ConversationMemoryService.Normalize(Search.Trim());
        if (query.Length == 0) return true;
        string searchable = ConversationMemoryService.Normalize($"{field.Section} {field.Label} {field.Description}");
        return searchable.Contains(query, StringComparison.Ordinal);
    }
    partial void OnSearchChanged(string value) => FilteredFields.Refresh();
    partial void OnSelectedSectionChanged(string value)
    {
        FilteredFields.Refresh();
        IsResetConfirmationOpen = false;
    }
    [RelayCommand] private void ClearSearch() => Search = "";
    [RelayCommand] private void ResetSection()
    {
        if (SelectedSection == "Wszystkie")
        {
            Status = "Wybierz kategorię przed jej przywróceniem.";
            return;
        }
        ResetConfirmationText = $"Przywrócić wartości domyślne kategorii „{SelectedSection}”? Niestandardowe ustawienia tej kategorii zostaną zastąpione.";
        IsResetConfirmationOpen = true;
    }
    [RelayCommand] private void CancelReset()
    {
        IsResetConfirmationOpen = false;
        ResetConfirmationText = "";
    }
    [RelayCommand] private void ConfirmResetSection()
    {
        if (SelectedSection == "Wszystkie")
        {
            Status = "Wybierz jedną kategorię przed przywróceniem wartości domyślnych.";
            CancelReset();
            return;
        }
        string section = SelectedSection;
        store.ResetSection(section);
        Rebuild();
        Status = store.LastError ?? $"Przywrócono domyślne wartości kategorii {section}.";
        CancelReset();
    }
    private void Rebuild() { Fields.Clear(); foreach (var field in SettingsCatalog.Create(store)) Fields.Add(new(field, store)); FilteredFields.Refresh(); }
}
