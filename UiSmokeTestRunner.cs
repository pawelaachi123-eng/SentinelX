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
            var vm = services.GetRequiredService<MainViewModel>();
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
            File.WriteAllText(Path.Combine(output, "ui-smoke.txt"), "PASS\nPages: " + string.Join(", ", visited) + "\nDark/DeepDark/System themes rendered\nSTOP/Resume/voice approval passed\n");
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
    }
}
