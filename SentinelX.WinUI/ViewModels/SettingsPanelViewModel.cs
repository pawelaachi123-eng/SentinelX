using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Settings;

namespace SentinelX.WinUI.ViewModels;

public sealed record SettingsSection(string Key, string Label);

/// <summary>
/// Settings flyout: General / Voice / AI / Sentinel Watch / Appearance / Advanced.
/// Rows are generated from the shared <see cref="SettingField"/> catalogue — the same
/// settings the WPF build edits, with per-field validation and save.
/// </summary>
public sealed partial class SettingsPanelViewModel : ObservableObject, IDisposable
{
    private readonly AppSettingsService store;
    private readonly ISettingsService settings;
    private readonly IUiDispatcher dispatcher;
    private readonly IReadOnlyList<SettingField> fields;
    private bool disposed;

    public ObservableCollection<SettingsSection> Sections { get; } =
    [
        new SettingsSection("general", "Ogólne"),
        new SettingsSection("voice", "Głos"),
        new SettingsSection("ai", "AI"),
        new SettingsSection("watch", "Sentinel Watch"),
        new SettingsSection("appearance", "Wygląd"),
        new SettingsSection("advanced", "Zaawansowane")
    ];

    public ObservableCollection<SettingRow> Rows { get; } = [];

    [ObservableProperty] private SettingsSection? selectedSection;
    [ObservableProperty] private string storeError = "";

    public SettingsPanelViewModel(AppSettingsService store, ISettingsService settings, IUiDispatcher dispatcher)
    {
        this.store = store;
        this.settings = settings;
        this.dispatcher = dispatcher;
        fields = SettingsCatalog.Create(store);
        settings.Changed += OnStoreChanged;
        SelectedSection = Sections[0];
    }

    partial void OnSelectedSectionChanged(SettingsSection? value) => RebuildRows();

    public void OpenSection(string key)
    {
        var section = Sections.FirstOrDefault(s => s.Key == key);
        if (section != null) SelectedSection = section;
    }

    private void OnStoreChanged() => dispatcher.Post(() =>
    {
        if (disposed) return;
        StoreError = settings.LastError ?? "";
        RebuildRows();
    });

    private void RebuildRows()
    {
        Rows.Clear();
        if (SelectedSection == null) return;
        foreach (var field in fields.Where(f => MapSection(f.Section) == SelectedSection.Key))
            Rows.Add(new SettingRow(field, store));
        StoreError = settings.LastError ?? "";
    }

    private static string MapSection(string catalog) => catalog switch
    {
        "Ogólne" => "general",
        "Głos" => "voice",
        "AI" => "ai",
        "Watch" => "watch",
        "Wygląd" => "appearance",
        _ => "advanced"
    };

    [RelayCommand]
    private void ResetSection()
    {
        if (SelectedSection == null) return;
        string[] catalog = SelectedSection.Key switch
        {
            "general" => ["Ogólne"],
            "voice" => ["Głos"],
            "ai" => ["AI"],
            "watch" => ["Watch"],
            "appearance" => ["Wygląd"],
            _ => ["Zasoby", "Developer", "Telefon", "Pamięć"]
        };
        foreach (string name in catalog) settings.ResetSection(name);
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(SentinelX.AppPaths.Root);

    [RelayCommand]
    private void OpenLogsFolder() => OpenFolder(Path.Combine(SentinelX.AppPaths.Root, "Logs"));

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SentinelX.AppLog.Write(ex);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        settings.Changed -= OnStoreChanged;
    }
}

public sealed partial class SettingRow : ObservableObject
{
    private readonly SettingField field;
    private readonly AppSettingsService store;
    private bool loading = true;

    public string Label => field.Label;
    public string Description => field.Description;
    public bool IsToggle => field.IsToggle;
    public string[]? Choices => field.Choices;
    public bool ShowChoices => !field.IsToggle && field.Choices != null;
    public bool ShowText => !field.IsToggle && field.Choices == null;

    [ObservableProperty] private string textValue = "";
    [ObservableProperty] private bool boolValue;
    [ObservableProperty] private string error = "";

    public SettingRow(SettingField field, AppSettingsService store)
    {
        this.field = field;
        this.store = store;
        Reload();
        loading = false;
    }

    private void Reload()
    {
        try
        {
            TextValue = field.Read();
            BoolValue = bool.TryParse(TextValue, out bool parsed) && parsed;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    partial void OnBoolValueChanged(bool value)
    {
        if (loading || !IsToggle) return;
        Apply();
    }

    partial void OnTextValueChanged(string value)
    {
        // A choice change applies immediately; free text waits for the Save button / Enter.
        if (loading || Choices == null) return;
        Apply();
    }

    [RelayCommand]
    private void Apply()
    {
        try
        {
            string? failure = field.Write(IsToggle ? BoolValue.ToString() : TextValue.Trim());
            if (failure != null)
            {
                Error = failure;
                return;
            }

            store.Save();
            Error = store.LastError ?? "";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            SentinelX.AppLog.Write(ex);
        }
    }
}
