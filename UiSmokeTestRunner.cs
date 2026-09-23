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
            await Tests.ReleaseRegression.RunAsync(Path.Combine(output, "release"));
            await Tests.MemoryRegression.RunAsync(Path.Combine(output, "memory"));
            await Tests.ProjectRegression.RunAsync(Path.Combine(output, "projects"));
            await Tests.TaskRegression.RunAsync(Path.Combine(output, "tasks"));
            await Tests.DiagnosticSnapshotRegression.RunAsync(Path.Combine(output, "snapshots"));
            await Tests.AiStreamRegression.RunAsync(Path.Combine(output, "ai-stream"));
            var vm = services.GetRequiredService<MainViewModel>();
            vm.Readiness.IsOpen = false;
            if (vm.InitializeCommand.IsRunning) await vm.InitializeCommand.ExecutionTask!;
            var chat = services.GetRequiredService<CommandCenterViewModel>();
            chat.UserInput = "Sentinel, ile mam RAM?";
            await chat.SendMessageCommand.ExecuteAsync(null);
            if (chat.Messages.Last().ActionRecord?.Status != Models.ActionStatus.Verified)
                throw new InvalidOperationException("RAM fast path did not produce measurement evidence.");
            // Memory roundtrip through the real engine and the real page VM.
            var memoryEngine = services.GetRequiredService<IActionEngine>();
            await memoryEngine.ExecuteAsync("zapamiętaj smoke: ulubiony kolor to cyjan");
            var memoryVm = services.GetRequiredService<MemoryViewModel>();
            memoryVm.RefreshDataCommand.Execute(null);
            if (!memoryVm.Items.Any(x => x.Text.Contains("cyjan")))
                throw new InvalidOperationException("Memory note created in chat did not reach the Memory page.");
            memoryVm.TogglePinCommand.Execute(memoryVm.Items.First(x => x.Text.Contains("cyjan")));
            if (!memoryVm.Items.Any(x => x.Pinned)) throw new InvalidOperationException("Pinning did not persist to the list.");
            var memoryService = services.GetRequiredService<ConversationMemoryService>();
            if (memoryService.ActiveConversationTitle.Length == 0)
                throw new InvalidOperationException("Conversation title must be available.");
            chat.TogglePrivateModeCommand.Execute(null);
            if (!memoryService.PrivateMode) throw new InvalidOperationException("Private mode toggle must apply immediately.");
            chat.TogglePrivateModeCommand.Execute(null);
            if (memoryService.PrivateMode) throw new InvalidOperationException("Private mode toggle must turn back off.");
            // Projects roundtrip through the real DI graph and the real page VM.
            var projectService = services.GetRequiredService<ProjectService>();
            var projectVm = services.GetRequiredService<ProjectViewModel>();
            var other = projectService.Create("Smoke projekt pomocniczy", "", activate: true);
            if (other == null) throw new InvalidOperationException("Project creation through DI must work.");
            memoryService.AddNote("smoke izolacja ZXCVBNM-inny");
            var smoke = projectService.Create("Smoke projekt", "canary ZXCVBNM", activate: true);
            if (smoke == null) throw new InvalidOperationException("Second project creation must work.");
            projectVm.RefreshCommand.Execute(null);
            if (projectVm.Cards.Count(x => x.Name.StartsWith("Smoke projekt")) != 2)
                throw new InvalidOperationException("Projects created through the service did not reach the Projects page.");
            if (!projectVm.ActiveLine.Contains("Smoke projekt"))
                throw new InvalidOperationException("Active project banner must name the activated project.");
            // The project must isolate the memory context immediately, without a restart.
            var isolatedContext = memoryService.GetStableContext();
            if (isolatedContext.Contains("ZXCVBNM-inny"))
                throw new InvalidOperationException("Another project's note leaked into the active project's AI context.");
            if (!isolatedContext.Contains("cyjan"))
                throw new InvalidOperationException("Global notes must stay visible inside every project context.");
            projectService.Deactivate();
            projectService.Archive(smoke.Id);
            projectService.Archive(other.Id);
            // Tasks roundtrip through the real DI graph and the real page VM.
            var taskService = services.GetRequiredService<TaskService>();
            var taskVm = services.GetRequiredService<TaskViewModel>();
            foreach (var oldReminder in taskService.GetReminders().Where(x => x.Text.Contains("ZXCVBNM"))) taskService.DeleteReminder(oldReminder.Id);
            if (taskService.AddTask("smoke zadanie ZXCVBNM", TaskRecord.PriorityHigh, null, "") == null)
                throw new InvalidOperationException("Task creation through DI must work.");
            taskVm.RefreshCommand.Execute(null);
            if (!taskVm.Items.Any(x => x.Title.Contains("ZXCVBNM")))
                throw new InvalidOperationException("Task created through the service did not reach the Tasks page.");
            if (taskService.AddReminder("minione smoke", DateTime.Now.AddMinutes(-1), "") != null)
                throw new InvalidOperationException("A past reminder must be refused.");
            // Chat reminder flow is accept-only: the proposal alone must not persist anything.
            await memoryEngine.ExecuteAsync("przypomnij mi jutro o 18 o sprawdzeniu wiadomości ZXCVBNM");
            if (taskService.GetReminders().Any(x => x.Text.Contains("ZXCVBNM")))
                throw new InvalidOperationException("A reminder must not exist before the explicit yes.");
            await memoryEngine.ExecuteAsync("tak");
            if (!taskService.GetReminders().Any(x => x.Text.Contains("ZXCVBNM")))
                throw new InvalidOperationException("The explicit yes must materialize the proposed reminder.");
            taskVm.RefreshCommand.Execute(null);
            if (!taskVm.Reminders.Any(x => x.Text.Contains("ZXCVBNM")))
                throw new InvalidOperationException("Reminder created in chat did not reach the Tasks page.");
            // Diagnostic snapshots: chat capture, page roundtrip and a real comparison.
            var snapshotService = services.GetRequiredService<DiagnosticSnapshotService>();
            var diagnosticVm = services.GetRequiredService<DiagnosticViewModel>();
            var captured = await memoryEngine.ExecuteAsync("snapshot");
            if (!captured.Text.Contains("Zapisano odczyt"))
                throw new InvalidOperationException("Chat snapshot command did not store a reading: " + captured.Text);
            if (!(await memoryEngine.ExecuteAsync("snapshoty")).Text.Contains("Zapisane odczyty"))
                throw new InvalidOperationException("Chat snapshot list is not wired.");
            await snapshotService.CaptureAsync("smoke porównanie B");
            diagnosticVm.RefreshCommand.Execute(null);
            if (diagnosticVm.Items.Count < 2)
                throw new InvalidOperationException("Readings captured in chat did not reach the Diagnostics page.");
            diagnosticVm.FirstSnapshot = diagnosticVm.Items[^2];
            diagnosticVm.SecondSnapshot = diagnosticVm.Items[^1];
            diagnosticVm.CompareCommand.Execute(null);
            if (!diagnosticVm.HasComparison || diagnosticVm.Comparison.Length == 0)
                throw new InvalidOperationException("Snapshot comparison did not produce a report.");
            if (!diagnosticVm.Comparison.Contains("Porównanie snapshotów"))
                throw new InvalidOperationException("The comparison report lost its header.");
            // Conversation tools: in-conversation search and a verified Markdown export.
            if (!(await memoryEngine.ExecuteAsync("szukaj w rozmowie: snapshot")).Text.Contains("Znalezione w tej rozmowie"))
                throw new InvalidOperationException("In-conversation search is not wired.");
            if (!(await memoryEngine.ExecuteAsync("eksportuj rozmowę markdown")).Text.Contains("SHA-256"))
                throw new InvalidOperationException("Conversation Markdown export did not produce a verified file.");
            // Streaming surface: the stop control must answer honestly when nothing is generating.
            chat.StopGenerationCommand.Execute(null);
            if (!chat.Status.Contains("Nic teraz nie jest generowane"))
                throw new InvalidOperationException("Stop generation must say when nothing is running: " + chat.Status);
            int messagesBeforeRetry = chat.Messages.Count;
            await chat.RetryCommand.ExecuteAsync(null);
            if (chat.Messages.Count < messagesBeforeRetry + 2)
                throw new InvalidOperationException("Retry must add the repeated command and a new answer.");
            if (!chat.Messages[^2].Content.Contains("ponowione"))
                throw new InvalidOperationException("A retried command must be visibly marked as a retry.");
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
            var historyVm = services.GetRequiredService<HistoryViewModel>();
            await historyVm.RefreshCommand.ExecuteAsync(null);
            historyVm.StatusFilter = "VERIFIED";
            if (historyVm.FilteredEntries.Cast<ActionHistoryEntry>().Any(x => x.Status != "VERIFIED"))
                throw new InvalidOperationException("History status filter failed.");
            historyVm.StatusFilter = "Wszystkie";
            await historyVm.ExportCommand.ExecuteAsync("json");
            if (!historyVm.ExportSummary.Contains("SHA-256")) throw new InvalidOperationException("History export command is not wired.");
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
