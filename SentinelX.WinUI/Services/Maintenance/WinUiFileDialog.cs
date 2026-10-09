using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.UI.Xaml;

namespace SentinelX.Services.Maintenance;

public static class WinUiFileDialog
{
    public static Window? MainWindow { get; set; }

    public static async Task<string?> SaveJsonAsync()
    {
        if (MainWindow == null) return null;

        var picker = new FileSavePicker();
        picker.SuggestedFileName = "sentinel-diagnostics";
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });

        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(MainWindow));

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> OpenFileAsync(string extension)
    {
        if (MainWindow == null) return null;

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(extension);

        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(MainWindow));

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}