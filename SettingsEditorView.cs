using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SentinelX;

public sealed class SettingsEditorView : StackPanel
{
    private readonly AppSettingsService store;
    private readonly TextBox search = new() { Margin = new(0, 6, 0, 16), ToolTip = "Szukaj: głos, VAD, AI, nakładka…" };
    private readonly TextBlock status = new() { Margin = new(0, 0, 0, 12), Text = "Zmiany zapisują się po Enter lub wyjściu z pola." };
    private readonly StackPanel sections = new();
    private readonly List<(FrameworkElement View, string Search)> rows = [];
    private readonly List<(FrameworkElement View, List<FrameworkElement> Children)> groups = [];

    public SettingsEditorView(AppSettingsService store)
    {
        this.store = store;
        Children.Add(new TextBlock { Text = "Dopasuj Sentinela", FontSize = 25, FontWeight = FontWeights.Bold });
        Children.Add(new TextBlock { Text = "Głos, modele, wygląd i zasoby. Zmiany działają bez przebudowy programu.", Margin = new(0, 6, 0, 10) });
        var toolbar = new WrapPanel();
        AddButton(toolbar, "Eksport JSON", Export);
        AddButton(toolbar, "Import JSON", Import);
        Children.Add(toolbar);
        System.Windows.Automation.AutomationProperties.SetName(search, "Szukaj ustawień");
        Children.Add(new TextBlock { Text = "Szukaj ustawień", FontSize = 12 });
        search.MinHeight = 36; search.MaxHeight = 44;
        Children.Add(search); Children.Add(status); Children.Add(sections);
        search.TextChanged += (_, _) => Filter();
        Rebuild();
    }

    public void Rebuild()
    {
        sections.Children.Clear(); rows.Clear(); groups.Clear();
        foreach (var group in SettingsCatalog.Create(store).GroupBy(x => x.Section))
        {
            var panel = new StackPanel();
            var heading = new DockPanel { Margin = new(0, 0, 0, 8) };
            var reset = new Button { Content = "Przywróć sekcję", FontSize = 11, Padding = new(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Right };
            reset.Click += (_, _) => { store.ResetSection(group.Key); status.Text = store.LastError ?? "Przywrócono: " + group.Key; Rebuild(); };
            DockPanel.SetDock(reset, Dock.Right); heading.Children.Add(reset);
            var title = new TextBlock { Text = group.Key, FontSize = 20, FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); heading.Children.Add(title); panel.Children.Add(heading);
            var children = new List<FrameworkElement>();
            foreach (var field in group)
            {
                var row = new Grid { Margin = new(0, 6, 0, 12) };
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = new(210) });
                var label = new StackPanel { Margin = new(0, 0, 20, 0) };
                label.Children.Add(new TextBlock { Text = field.Label, FontWeight = FontWeights.SemiBold });
                var description = new TextBlock { Text = field.Description, FontSize = 12, Margin = new(0, 4, 0, 0) };
                description.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); label.Children.Add(description); row.Children.Add(label);
                FrameworkElement control;
                if (field.IsToggle)
                {
                    var toggle = new CheckBox { Content = "Włączone", IsChecked = bool.Parse(field.Read()), VerticalAlignment = VerticalAlignment.Center };
                    toggle.Click += (_, _) => Save(field, (toggle.IsChecked == true).ToString()); control = toggle;
                }
                else if (field.Choices != null)
                {
                    var combo = new ComboBox { ItemsSource = field.Choices, SelectedItem = field.Read(), Margin = new(0), VerticalAlignment = VerticalAlignment.Center };
                    combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) Save(field, value); }; control = combo;
                }
                else
                {
                    var input = new TextBox { Text = field.Read(), VerticalAlignment = VerticalAlignment.Center, Padding = new(10, 8, 10, 8) };
                    void Commit()
                    {
                        if (input.Text == field.Read()) return;
                        if (Save(field, input.Text)) input.Text = field.Read();
                    }
                    input.LostKeyboardFocus += (_, _) => Commit();
                    input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Commit(); e.Handled = true; } };
                    control = input;
                }
                System.Windows.Automation.AutomationProperties.SetName(control, field.Label);
                Grid.SetColumn(control, 1); row.Children.Add(control); panel.Children.Add(row); children.Add(row);
                rows.Add((row, ConversationMemoryService.Normalize(field.Section + " " + field.Label + " " + field.Description)));
            }
            var card = new Border { Child = panel, Margin = new(0, 0, 0, 16) };
            card.SetResourceReference(StyleProperty, "Card"); sections.Children.Add(card); groups.Add((card, children));
        }
        Filter();
    }

    private bool Save(SettingField field, string value)
    {
        string? error = field.Write(value);
        if (error != null) { status.Text = field.Label + ": " + error; status.Foreground = Brushes.Orange; return false; }
        store.Save(); status.Text = store.LastError ?? "Zapisano: " + field.Label + ". Ustawienie jest aktywne.";
        status.SetResourceReference(TextBlock.ForegroundProperty, store.LastError == null ? "Accent" : "Muted");
        return store.LastError == null;
    }
    private void Filter()
    {
        string query = ConversationMemoryService.Normalize(search.Text.Trim());
        foreach (var item in rows) item.View.Visibility = item.Search.Contains(query) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var group in groups) group.View.Visibility = group.Children.Any(x => x.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
    }
    private static void AddButton(Panel panel, string label, Action handler) { var b = new Button { Content = label }; b.Click += (_, _) => handler(); panel.Children.Add(b); }
    private void Export()
    {
        var dialog = new SaveFileDialog { FileName = "Sentinel-settings.json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { store.Export(dialog.FileName); status.Text = "Wyeksportowano ustawienia: " + dialog.FileName; }
        catch (Exception ex) { status.Text = ex.Message; AppLog.Write(ex); }
    }
    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "JSON|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        bool ok = store.Import(dialog.FileName);
        status.Text = ok ? "Zaimportowano i zweryfikowano ustawienia. Poprzednia wersja: settings.json.backup." : store.LastError;
        if (ok) Rebuild();
    }
}
