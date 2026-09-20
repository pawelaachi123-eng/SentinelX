using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Services.Actions;
using SentinelX.ViewModels;
namespace SentinelX;

/// <summary>Windows-only: instantiate and render every real page, then check bindings and stop semantics.</summary>
public static class UiSmokeTestRunner
{
    private static void Capture(Window shell, string path)
    {
        var bitmap = new RenderTargetBitmap((int)shell.ActualWidth, (int)shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(shell); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
    public static async Task RunAsync(IServiceProvider services, Window shell, string output)
    {
        Directory.CreateDirectory(output);
        using var buffer = new StringWriter();
        using var listener = new TextWriterTraceListener(buffer);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            await Tests.BackendRegression.RunAsync(Path.Combine(output, "backend"));
            await Tests.ProductRegression.RunAsync(Path.Combine(output, "product"));
            var vm = services.GetRequiredService<MainViewModel>();
            vm.Readiness.IsOpen = false;
            if (vm.InitializeCommand.IsRunning) await vm.InitializeCommand.ExecutionTask!;
            var chat = services.GetRequiredService<CommandCenterViewModel>();
            chat.UserInput = "Sentinel, ile mam RAM?";
            await chat.SendMessageCommand.ExecuteAsync(null);
            if (chat.Messages.Last().ActionRecord?.Status != Models.ActionStatus.Verified)
                throw new InvalidOperationException("RAM fast path did not produce measurement evidence.");
            var engine = services.GetRequiredService<IActionEngine>();
            await engine.ExecuteAsync("zamknij notatnik"); // requests permission only; never closes a process in CI.
            vm.SelectedItem = vm.NavItems[^1];
            var visited = new List<string>();
            foreach (var item in vm.NavItems)
            {
                vm.SelectedItem = item;
                await shell.Dispatcher.InvokeAsync(shell.UpdateLayout, DispatcherPriority.ContextIdle);
                await Task.Delay(150);
                visited.Add(item.Key);
                var image = new RenderTargetBitmap((int)shell.ActualWidth, (int)shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(shell);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                using var imageFile = File.Create(Path.Combine(output, item.Key + ".png")); png.Save(imageFile);
            }
            vm.OpenPaletteCommand.Execute(null);
            vm.Palette.Query = "ustawienia";
            await shell.Dispatcher.InvokeAsync(shell.UpdateLayout, DispatcherPriority.ContextIdle);
            Capture(shell, Path.Combine(output, "palette.png"));
            vm.Palette.ChooseCommand.Execute(null);
            if (vm.SelectedItem?.Key != "settings") throw new InvalidOperationException("Palette navigation not wired.");
            vm.OpenPaletteCommand.Execute(null); vm.Palette.Query = "użycie CPU";
            vm.Palette.ChooseCommand.Execute(null);
            if (chat.UserInput != "użycie CPU") throw new InvalidOperationException("Palette must stage a command.");
            var before = chat.Messages.Count;
            await shell.Dispatcher.InvokeAsync(shell.UpdateLayout, DispatcherPriority.ContextIdle);
            if (chat.Messages.Count != before) throw new InvalidOperationException("Palette must not auto-execute.");
            engine.EmergencyStop();
            await chat.SendMessageCommand.ExecuteAsync(null);
            if (chat.UserInput != "użycie CPU") throw new InvalidOperationException("STOP must preserve the draft.");
            engine.Resume();
            await shell.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var previousFocus = System.Windows.Input.Keyboard.FocusedElement;
            vm.OpenPaletteCommand.Execute(null);
            await shell.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.TextBox search || search.Name != "SearchInput")
                throw new InvalidOperationException("Palette search did not receive keyboard focus.");
            vm.Palette.CloseCommand.Execute(null);
            await shell.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (previousFocus != null && !ReferenceEquals(previousFocus, System.Windows.Input.Keyboard.FocusedElement))
                throw new InvalidOperationException("Palette did not restore keyboard focus.");
            vm.OpenReadinessCommand.Execute(null);
            await vm.Readiness.RefreshCommand.ExecuteAsync(null);
            await shell.Dispatcher.InvokeAsync(shell.UpdateLayout, DispatcherPriority.ContextIdle);
            if (vm.Readiness.Checks.Count != 4) throw new InvalidOperationException("Readiness cards not populated.");
            Capture(shell, Path.Combine(output, "readiness.png"));
            vm.Readiness.CloseCommand.Execute(null);
            foreach (string theme in new[] { "Deep Dark", "System", "Dark" })
            {
                var store = services.GetRequiredService<AppSettingsService>();
                store.Settings.Ui.Theme = theme; store.Save();
                shell.UpdateLayout();
            }
            services.GetRequiredService<Services.Desktop.IDesktopService>().ToggleOverlay();
            await shell.Dispatcher.InvokeAsync(shell.UpdateLayout, DispatcherPriority.ContextIdle);
            services.GetRequiredService<Services.Desktop.IDesktopService>().ToggleOverlay();
            engine.EmergencyStop();
            var blocked = await engine.ExecuteAsync("uruchom kalkulator");
            if (!engine.IsStopped || blocked.Action != null) throw new InvalidOperationException("STOP did not block an action.");
            engine.Resume();
            if (engine.IsStopped) throw new InvalidOperationException("Resume failed.");
            var voiceApproval = await engine.ExecuteAsync("potwierdz", fromVoice: true);
            if (voiceApproval.Action != null) throw new InvalidOperationException("Voice must never approve actions.");
            listener.Flush();
            string errors = buffer.ToString();
            File.WriteAllText(Path.Combine(output, "bindings.log"), errors);
            if (errors.Length != 0) throw new InvalidOperationException("WPF binding errors: " + errors);
            File.WriteAllText(Path.Combine(output, "ui-smoke.txt"), "PASS\nPages: " + string.Join(", ", visited) + "\nDark/DeepDark/System themes rendered\nSTOP/Resume/voice approval passed\nPalette, readiness, draft preservation and execution-scoped evidence passed\n");
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
    }
}
