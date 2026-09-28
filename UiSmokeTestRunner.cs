using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
            await Tests.ContextRegression.RunAsync(Path.Combine(output, "context"));
            await Tests.SelfMaintenanceRegression.RunAsync(Path.Combine(output, "self-maintenance"));
            await Tests.TaskRegression.RunAsync(Path.Combine(output, "tasks"));
            await Tests.DiagnosticSnapshotRegression.RunAsync(Path.Combine(output, "snapshots"));
            await Tests.AiStreamRegression.RunAsync(Path.Combine(output, "ai-stream"));
            await Tests.UnderstandingRegression.RunAsync(Path.Combine(output, "understanding"));
            await Tests.UtilityRegression.RunAsync(Path.Combine(output, "utility"));
            await Tests.JarvisRegression.RunAsync(Path.Combine(output, "jarvis"));
            await Tests.FileCleanupRegression.RunAsync(Path.Combine(output, "file-cleanup"));
            await Tests.MemoryArchiveRegression.RunAsync(Path.Combine(output, "archives"));
            await Tests.CoreRuntimeRegression.RunAsync(Path.Combine(output, "core"));
            await Tests.DeveloperToolboxRegression.RunAsync(Path.Combine(output, "developer-tools"));
            await Tests.ModelToolboxRegression.RunAsync(Path.Combine(output, "models"));
            await Tests.ModelManagementRegression.RunAsync(Path.Combine(output, "model-management"));
            await Tests.ArchitectureToolboxRegression.RunAsync(Path.Combine(output, "architecture"));
            await Tests.ResearchToolboxRegression.RunAsync(Path.Combine(output, "research"));
            await Tests.KnowledgeToolboxRegression.RunAsync(Path.Combine(output, "knowledge"));
            await Tests.GoalToolboxRegression.RunAsync(Path.Combine(output, "goals"));
            await Tests.FullStackToolboxRegression.RunAsync(Path.Combine(output, "fullstack"));
            await Tests.SmartHomeToolboxRegression.RunAsync(Path.Combine(output, "smarthome"));
            await Tests.MediaVisionToolboxRegression.RunAsync(Path.Combine(output, "media-vision"));
            await Tests.EverydayRegression.RunAsync(Path.Combine(output, "everyday"));
            await Tests.ChartRegression.RunAsync(Path.Combine(output, "charts"));
            await Tests.KnowledgeIndexRegression.RunAsync(Path.Combine(output, "knowledge-index"));
            await Tests.RagRegression.RunAsync(Path.Combine(output, "rag"));
            await Tests.WebAccessRegression.RunAsync(Path.Combine(output, "web-access"));
            await Tests.GameDevRegression.RunAsync(Path.Combine(output, "gamedev"));
            await Tests.GameAssetsRegression.RunAsync(Path.Combine(output, "game-assets"));
            await Tests.GameForgeRegression.RunAsync(Path.Combine(output, "game-forge"));
            await Tests.AnalysisToolboxRegression.RunAsync(Path.Combine(output, "analysis"));
            await Tests.LifeToolboxRegression.RunAsync(Path.Combine(output, "life"));
            await Tests.PrivacyToolboxRegression.RunAsync(Path.Combine(output, "privacy"));
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
            string snapshotList = (await memoryEngine.ExecuteAsync("snapshoty")).Text;
            if (!snapshotList.Contains("Zapisane odczyty"))
                throw new InvalidOperationException("Chat snapshot list is not wired. Odpowiedź: " + snapshotList.Replace(Environment.NewLine, " | "));
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
            // Typo tolerance: the repaired command must run and the interpretation must be disclosed.
            var repaired = await memoryEngine.ExecuteAsync("ile mam ramuu");
            if (!repaired.Text.Contains("Zrozumiałem jako") || !repaired.Text.Contains("ile mam ramu"))
                throw new InvalidOperationException("A mistyped command must be understood and disclosed: " + repaired.Text);
            if (!(await memoryEngine.ExecuteAsync("ststus pamieci")).Text.Contains("Zrozumiałem jako"))
                throw new InvalidOperationException("A mistyped memory command must be disclosed as a repair.");
            // Offline tools through the real chat pipeline.
            foreach (var (command, expected) in new[]
            {
                ("policz 12+8", "= 20"), ("przelicz 5 km na mile", "3,1069"), ("procent 15 z 240", "= 36"),
                ("ile to procent 30 z 240", "12,5%"), ("vat 100", "brutto 123,00"), ("haslo 20", "entropii"),
                ("uuid", "UUID: "), ("ile slow: ala ma kota", "Słowa: 3"), ("base64: ala", "YWxh"),
                ("hash tekstu: abc", "ba7816bf8f01cfea"), ("json: {\"a\":1}", "JSON poprawny"),
                ("slug: ZaŻółć Gęślą Jaźń", "zazolc-gesla-jazn"), ("wielkie litery: kot", "KOT"),
                ("odwroc tekst: kot", "tok"), ("losuj 1-6", "Wylosowano"), ("rzuc kostka", "suma:"),
                ("wybierz losowo: pizza, sushi", "Wybrano:"), ("bmi 80 180", "BMI 24,7"),
                ("rzymskie 2026", "MMXXVI"), ("z rzymskich MMXXVI", "= 2026"), ("kolor 1fa2c3", "RGB(31, 162, 195)"),
                ("jaki dzien tygodnia 1.1.2030", "wtorek"), ("plan dnia", "PLAN NA"), ("statystyki", "STATYSTYKI"),
                ("skroty", "→"), ("pomoc", "CO UMIEM"), ("archiwa", "archiw"), ("backup", "SHA-256"),
                ("pierwiastek 144", "= 12"), ("silnia 10", "3628800"), ("nwd 12 8", "= 4"),
                ("palindrom: kajak", "palindromem"), ("morse: sos", "... --- ..."),
                ("pesel: 90010112349", "PESEL poprawny"), ("wielkanoc 2027", "28.03.2027"),
                ("lotto", "Lotto (6 z 49)"), ("wersja", Core.AppConstants.Version), ("co nowego", "CO NOWEGO W " + Core.AppConstants.Version),
                ("nazwa komputera", "Komputer:"), ("samokontrola", "SAMOKONTROLA"),
            })
            {
                string toolResponse = (await memoryEngine.ExecuteAsync(command)).Text;
                if (!toolResponse.Contains(expected))
                    throw new InvalidOperationException("Tool command „" + command + "” did not answer as expected (" + expected + "): " + toolResponse);
            }
            // Unified search must reach every module, not just the conversation.
            string unified = (await memoryEngine.ExecuteAsync("szukaj wszystkiego: cyjan")).Text;
            if (!unified.Contains("Znalezione w danych lokalnych"))
                throw new InvalidOperationException("Unified search is not wired: " + unified);
            // 0.91 · CENTRUM: the grey zone must ask instead of guessing, and an explicit „tak” runs the known command.
            var ambiguous = await memoryEngine.ExecuteAsync("ile mam ramu dzis");
            if (!ambiguous.Text.Contains("Czy chodziło Ci o") || !ambiguous.Text.Contains("ile mam ramu"))
                throw new InvalidOperationException("An ambiguous command must produce a question, not a guess: " + ambiguous.Text);
            var confirmed = await memoryEngine.ExecuteAsync("tak");
            if (!confirmed.Text.Contains("GiB"))
                throw new InvalidOperationException("Saying „tak” to a suggestion must execute the known command: " + confirmed.Text);
            // 0.91 · WAKE-WORD: commands run only when „sentinel” appears — anywhere in the sentence.
            if (!SentinelX.Core.CommandText.ContainsWakeWord("ile mam ramu sentinel"))
                throw new InvalidOperationException("Wake word must be recognized anywhere in the sentence.");
            if (SentinelX.Core.CommandText.ContainsWakeWord("sentinelowy kot"))
                throw new InvalidOperationException("Wake word must match whole words only.");
            if (SentinelX.Core.CommandText.StripWakeWord("ile mam ramu, sentinel") != "ile mam ramu")
                throw new InvalidOperationException("Wake word must be removable from any position.");
            if (SentinelX.Core.CommandText.StripWakeWord("Sentinel, ile mam RAM") != "ile mam RAM")
                throw new InvalidOperationException("Wake word must still be removable from the sentence start.");
            // 0.92 · PLIKI: safe file work through the real engine — duplicate scan stays read-only.
            string filesDir = Path.Combine(output, "smoke-files");
            Directory.CreateDirectory(filesDir);
            File.WriteAllText(Path.Combine(filesDir, "jeden.txt"), "ta sama tresc QX77");
            File.WriteAllText(Path.Combine(filesDir, "dwa.txt"), "ta sama tresc QX77");
            string dupReport = (await memoryEngine.ExecuteAsync("duplikaty: " + filesDir)).Text;
            if (!dupReport.Contains("Grup duplikatów: 1") || !dupReport.Contains("niczego nie usuwam"))
                throw new InvalidOperationException("Duplicate scan must report groups and stay read-only: " + dupReport);
            string bareDup = (await memoryEngine.ExecuteAsync("duplikaty")).Text;
            if (!bareDup.Contains("Podaj folder"))
                throw new InvalidOperationException("A bare „duplikaty” must show usage, not fall through to the model: " + bareDup);
            // 0.91: „zrob zadanie: …” is an explicit command — it lands in the Tasks tab, not only in chat.
            var madeTask = await memoryEngine.ExecuteAsync("zrob zadanie: przetestowac centrum QX77");
            if (!madeTask.Text.Contains("Zadanie zapisane"))
                throw new InvalidOperationException("zrob zadanie must create a task directly: " + madeTask.Text);
            taskVm.RefreshCommand.Execute(null);
            if (!taskVm.Items.Any(x => x.Title.Contains("QX77")))
                throw new InvalidOperationException("A task created with „zrob zadanie” did not reach the Tasks tab.");
            // 0.91: quick note alias, lessons journal, self-check and suggestions through the real engine.
            await memoryEngine.ExecuteAsync("notatka: ulubiona kawa to flat white QX77");
            memoryVm.RefreshDataCommand.Execute(null);
            if (!memoryVm.Items.Any(x => x.Text.Contains("flat white QX77")))
                throw new InvalidOperationException("A note created with „notatka:” did not reach the Memory page.");
            string lessons = (await memoryEngine.ExecuteAsync("lekcje")).Text;
            if (!lessons.Contains("LEKCJE") || !lessons.Contains("ramuu"))
                throw new InvalidOperationException("The lessons journal must show the earlier typo repair: " + lessons);
            string selfCheck = (await memoryEngine.ExecuteAsync("samokontrola")).Text;
            if (!selfCheck.Contains("SAMOKONTROLA") || !selfCheck.Contains("tylko do odczytu"))
                throw new InvalidOperationException("Self-check report is not wired: " + selfCheck);
            string suggestions = (await memoryEngine.ExecuteAsync("propozycje")).Text;
            if (!suggestions.Contains("PROPOZYCJE") || !suggestions.Contains("czeka na Twoją decyzję"))
                throw new InvalidOperationException("Suggestions must stay informational only: " + suggestions);
            // 0.91: honest capability boundaries — refusals instead of invented abilities.
            string modelRefusal = (await memoryEngine.ExecuteAsync("zbuduj model 3d")).Text;
            if (!modelRefusal.Contains("nie buduję modeli 3D"))
                throw new InvalidOperationException("3D modeling must be refused honestly: " + modelRefusal);
            string selfModRefusal = (await memoryEngine.ExecuteAsync("ulepsz sie")).Text;
            if (!selfModRefusal.Contains("Nie modyfikuję własnego kodu"))
                throw new InvalidOperationException("Self-code modification must be refused honestly: " + selfModRefusal);
            if (!selfModRefusal.Contains("uczę") && !selfModRefusal.Contains("Nauczone"))
                throw new InvalidOperationException("Self-improvement must describe the learning loop: " + selfModRefusal);
            // 0.95: self-repair fixes data only and reports every step.
            string selfRepair = (await memoryEngine.ExecuteAsync("napraw sie")).Text;
            if (!selfRepair.Contains("SELF-REPAIR") || !selfRepair.Contains("nigdy kod"))
                throw new InvalidOperationException("Self-repair must be wired and honest about scope: " + selfRepair);
            // 0.95: timers arm immediately (the command IS the request), the stopwatch answers directly.
            string timerReply = (await memoryEngine.ExecuteAsync("timer 5 minut test")).Text;
            if (!timerReply.Contains("Timer ustawiony"))
                throw new InvalidOperationException("A timer must arm immediately: " + timerReply);
            string stopwatchReply = (await memoryEngine.ExecuteAsync("stoper start")).Text +
                (await memoryEngine.ExecuteAsync("stoper")).Text + (await memoryEngine.ExecuteAsync("stoper stop")).Text;
            if (!stopwatchReply.Contains("Stoper wystartował") || !stopwatchReply.Contains("Stoper zatrzymany"))
                throw new InvalidOperationException("The stopwatch must start, report and stop: " + stopwatchReply);
            // 0.96: JARVIS over the desktop, routines and the daily briefing — all local, all honest.
            string windowsReply = (await memoryEngine.ExecuteAsync("okna")).Text;
            if (!windowsReply.Contains("Otwarte okna") && !windowsReply.Contains("Nie widzę"))
                throw new InvalidOperationException("The window list must be answered or refused honestly: " + windowsReply);
            string routinesReply = (await memoryEngine.ExecuteAsync("rutyny")).Text;
            if (!routinesReply.Contains("Rutyny") || !routinesReply.Contains("poranek"))
                throw new InvalidOperationException("Starter routines must be listed: " + routinesReply);
            string routineRun = (await memoryEngine.ExecuteAsync("uruchom rutynę: poranek")).Text;
            if (!routineRun.Contains("Rutyna") || !routineRun.Contains("Koniec rutyny"))
                throw new InvalidOperationException("A routine must run its steps and report them: " + routineRun);
            string routineRefusal = (await memoryEngine.ExecuteAsync("dodaj rutynę: zla = usuń wszystkie wspomnienia")).Text;
            if (!routineRefusal.Contains("niszczących"))
                throw new InvalidOperationException("A destructive routine step must be refused: " + routineRefusal);
            string briefing = (await memoryEngine.ExecuteAsync("dzień dobry")).Text;
            if (!briefing.Contains("Dzień dobry") || !briefing.Contains("KOMPUTER"))
                throw new InvalidOperationException("The morning briefing must read local facts: " + briefing);
            string pomodoro = (await memoryEngine.ExecuteAsync("pomodoro 45")).Text;
            if (!pomodoro.Contains("Pomodoro") || !pomodoro.Contains("przypomn"))
                throw new InvalidOperationException("Pomodoro must arm an honest countdown: " + pomodoro);
            string clipboardHistory = (await memoryEngine.ExecuteAsync("historia schowka")).Text;
            if (!clipboardHistory.Contains("Historia schowka"))
                throw new InvalidOperationException("The clipboard history must answer: " + clipboardHistory);
            string findFile = (await memoryEngine.ExecuteAsync("znajdź plik: sentinel-smoke-0.96")).Text;
            if (!findFile.Contains("znajd") && !findFile.Contains("Znalazłem") && !findFile.Contains("Nie znalazłem"))
                throw new InvalidOperationException("File search must answer: " + findFile);
            if (SentinelX.Core.SlashCatalog.TryResolve("rutyny")?.Target != "rutyny")
                throw new InvalidOperationException("//rutyny must map to the routines command.");
            // 0.91: „//” shortcuts resolve through the catalogue without touching the AI.
            string slashHelp = (await memoryEngine.ExecuteAsync("//pomoc")).Text;
            if (!slashHelp.Contains("CO UMIEM"))
                throw new InvalidOperationException("//pomoc must resolve to the help command: " + slashHelp);
            if (SentinelX.Core.SlashCatalog.TryResolve("diag")?.Target != "diagnostyka komputera")
                throw new InvalidOperationException("//diag must map to the full diagnostic command.");
            // 0.91: voice listens by default after launch (explicit user decision; visible indicator + one-click stop).
            if (!services.GetRequiredService<AppSettingsService>().Settings.Startup.StartVoiceOnLaunch)
                throw new InvalidOperationException("Voice must be on by default in 0.91.");
            // 0.91: the // palette opens on „//”, Tab cycles it, and it closes on cleared input.
            chat.UserInput = "//";
            if (!chat.SlashOpen || chat.SlashItems.Count == 0)
                throw new InvalidOperationException("Typing // must open the command palette.");
            int slashStart = chat.SlashIndex;
            chat.SlashNextCommand.Execute(null);
            if (chat.SlashIndex == slashStart && chat.SlashItems.Count > 1)
                throw new InvalidOperationException("Tab (SlashNext) must cycle the palette selection.");
            chat.UserInput = "//diag";
            if (chat.SlashItems.All(x => x.Entry.Trigger != "diag"))
                throw new InvalidOperationException("Filtering //diag must list the diagnostic entry.");
            chat.UserInput = "";
            if (chat.SlashOpen) throw new InvalidOperationException("Clearing the input must close the palette.");
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
            // 0.97 · STUDIO: strona narzędzi musi realnie uruchamiać narzędzia nowych sekcji,
            // a nie tylko je wyświetlać. Sprawdzam też historię, czyszczenie i to, że schowek
            // odmawiający pracy (sesja CI bez interaktywnego pulpitu) nie wywala aplikacji.
            var studio = services.GetRequiredService<StudioViewModel>();
            if (studio.Categories.Count < 6 || studio.Categories.Sum(x => x.Samples.Count) < 60)
                throw new InvalidOperationException("Studio nie ma pełnego katalogu narzędzi: " + studio.Categories.Count + " kategorii, " + studio.Categories.Sum(x => x.Samples.Count) + " poleceń.");
            if (studio.Categories.Any(x => x.Samples.Count == 0) || studio.Categories.Any(x => x.Samples.Any(y => y.Note.Length < 10)))
                throw new InvalidOperationException("Każde polecenie w Studiu musi mieć opis — inaczej lista jest ślepa.");
            studio.ShowCategory("prywatnosc");
            if (studio.SelectedCategory.Key != "prywatnosc" || !studio.SelectedCategoryLine.Contains("10"))
                throw new InvalidOperationException("Wybór kategorii Studia (paleta „studio:…”) nie działa.");
            var staged = studio.Categories.SelectMany(x => x.Samples).First(x => x.Command.StartsWith("bmr"));
            studio.UseSampleCommand.Execute(staged);
            if (!studio.Input.StartsWith("bmr") || !studio.Status.Contains("Mifflin"))
                throw new InvalidOperationException("Kliknięcie gotowego polecenia nie wypełnia pola wraz z opisem.");
            await studio.RunCommand.ExecuteAsync(null);
            if (!studio.Result.Contains("1780 kcal"))
                throw new InvalidOperationException("Studio nie policzyło BMR. Wynik: " + studio.Result.Replace(Environment.NewLine, " | "));
            studio.ShowCategory("analiza");
            studio.Input = "statystyki liczb: 3 4 4 5 9 12";
            await studio.RunCommand.ExecuteAsync(null);
            if (!studio.Result.Contains("mediana: 4,5"))
                throw new InvalidOperationException("Studio nie policzyło statystyk. Wynik: " + studio.Result.Replace(Environment.NewLine, " | "));
            if (studio.Recent.Count < 2 || !studio.HasRecent || !studio.ResultHeader.Contains("statystyki liczb"))
                throw new InvalidOperationException("Historia uruchomień Studia nie działa.");
            studio.CopyCommand.Execute(null);
            studio.ClearCommand.Execute(null);
            if (studio.Input.Length != 0 || studio.Result.Length != 0 || studio.SelectedSample != null)
                throw new InvalidOperationException("Czyszczenie Studia nie działa.");
            // 0.91 · CENTRUM: every embedded tab inside Centrum must render without binding errors.
            foreach (var tab in chat.Sections)
            {
                chat.SelectedTab = tab;
                await shell.Dispatcher.InvokeAsync(shell.UpdateLayout, DispatcherPriority.ContextIdle);
                await Task.Delay(150);
                var tabImage = new RenderTargetBitmap((int)shell.ActualWidth, (int)shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                tabImage.Render(shell);
                var tabPng = new PngBitmapEncoder(); tabPng.Frames.Add(BitmapFrame.Create(tabImage));
                using var tabFile = File.Create(Path.Combine(output, "centrum-" + tab.Key + ".png")); tabPng.Save(tabFile);
                visited.Add("centrum:" + tab.Key);
            }
            chat.SelectedTab = chat.Sections[0];
            await shell.Dispatcher.InvokeAsync(shell.UpdateLayout, DispatcherPriority.ContextIdle);
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
            // 0.93 · NOWOCZESNE GUI: asercje warstwy wizualnej.
            // Motyw po zastosowaniu musi dalej zawierać komplet tokenów koloru,
            // a tokeny układu i animacje muszą być osiągalne z App.Resources.
            string[] tokenColors = ["SxBackgroundColor", "SxSurfaceColor", "SxSurfaceSunkenColor", "SxSurfaceActiveColor",
                "SxTextPrimaryColor", "SxTextSecondaryColor", "SxTextMutedColor", "SxAccentCyanColor", "SxAccentVioletColor",
                "SxAccentTealColor", "SxAccentLimeColor", "SxAccentPinkColor", "SxAccentBlueColor", "SxSuccessColor", "SxWarningColor",
                "SxErrorColor", "SxBorderColor", "SxBorderSubtleColor", "SxBorderStrongColor", "SxScrimColor", "SxOverlayBgColor"];
            foreach (string token in tokenColors)
                if (Application.Current.TryFindResource(token) is not Color) throw new InvalidOperationException("Theme token missing after theme switch: " + token);
            string[] tokenLayout = ["SxRadiusXs", "SxRadiusSm", "SxRadiusMd", "SxRadiusLg", "SxRadiusCircle", "SxRadiusModalTop",
                "SxRadiusCardEdge", "SxSpaceXs", "SxSpaceSm", "SxSpaceMd", "SxSpaceLg", "SxSpaceXl", "SxSpaceCard", "SxSpacePage",
                "SxHairlineThickness", "SxEdgeThickness"];
            foreach (string token in tokenLayout)
                if (Application.Current.TryFindResource(token) is not (CornerRadius or Thickness)) throw new InvalidOperationException("Layout token missing: " + token);
            string[] tokenStyles = ["SxPrimaryButton", "SxSecondaryButton", "SxDangerButton", "SxGhostButton", "SxChipButton", "SxPillButton",
                "SxNavTile", "SxSidebarItem", "SxListItem", "SxTabItem", "SxIconChip", "SxIconChipAccent", "SxPageWash", "SxCard",
                "SxCardElevated", "SxCardInteractive", "SxMetricCard", "SxAccentCard", "SxPanelSunken", "SxModalCard", "SxBadge",
                "SxBadgeAccent", "SxBadgeSuccess", "SxBadgeWarning", "SxBadgeError", "SxBadgeInfo", "SxEmptyState", "SxHeaderAccentBar",
                "SxDivider", "SxDividerAccent", "SxTextBox", "SxProgressBar", "SxProgressBarSlim", "SxStatusDot", "SxShimmerBar",
                "SxBubbleAssistant", "SxBubbleUser", "SxBubbleStreaming", "SxGradientTitle", "SxEyebrow", "SxMetricValue", "SxMono"];
            foreach (string token in tokenStyles)
                if (Application.Current.TryFindResource(token) is not Style) throw new InvalidOperationException("Design token style missing: " + token);
            foreach (string token in new[] { "ReadinessBrush", "StatusBrush", "VoiceBrush", "RiskBrush", "BoolToVisibility", "StringNotEmptyToVisibility", "SafePercent", "FiniteToVisibility" })
                if (Application.Current.TryFindResource(token) is not System.Windows.Data.IValueConverter) throw new InvalidOperationException("Converter missing: " + token);
            string[] tokenBoards = ["SxPageIn", "SxModalIn", "SxScrimIn", "SxFadeIn", "SxFadeOut", "SxHoverIn", "SxHoverOut", "SxPressIn",
                "SxPressOut", "SxGlowIn", "SxGlowOut", "SxSheenIn", "SxLiftIn", "SxLiftOut", "SxNavBarGrow", "SxNavBarShrink", "SxLineGrow",
                "SxLineShrink", "SxPulse", "SxPulseSoft", "SxBreathe", "SxShimmerSlide", "SxStripeSlide", "SxAuroraDriftA", "SxAuroraDriftB", "SxAuroraDriftC", "SxFadeSlideIn"];
            foreach (string token in tokenBoards)
                if (Application.Current.TryFindResource(token) is not Storyboard) throw new InvalidOperationException("Animation storyboard missing: " + token);
            if (Application.Current.TryFindResource("SxAnimationsEnabled") is not bool) throw new InvalidOperationException("Animation gate token missing.");
            string[] tokenBrushes = ["SxAccentGradient", "SxVioletPinkGradient", "SxTealLimeGradient", "SxBlueCyanGradient",
                "SxDangerGradient", "SxSuccessGradient", "SxWarningGradient", "SxShimmerGradient", "SxRevealBrush",
                "SxAuroraCyanBrush", "SxAuroraVioletBrush", "SxAuroraPinkBrush"];
            foreach (string token in tokenBrushes)
                if (Application.Current.TryFindResource(token) is not Brush) throw new InvalidOperationException("Gradient token missing: " + token);
            foreach (string token in new[] { "SxShadowSoft", "SxShadowRaised", "SxShadowModal", "SxGlowAccent" })
                if (Application.Current.TryFindResource(token) is not System.Windows.Media.Effects.Effect) throw new InvalidOperationException("Effect token missing: " + token);

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
            File.WriteAllText(Path.Combine(output, "ui-smoke.txt"), "PASS\nPages: " + string.Join(", ", visited) + "\nCentrum tabs, // palette and voice default verified\nDark/DeepDark/System themes rendered\nSTOP/Resume/voice approval passed\nPalette, readiness, draft preservation and execution-scoped evidence passed\nTypo repair, grey-zone questions, lessons, self-check, offline tools, archives, insights and unified search passed\nAnalysis (section 15), health and communication (sections 17 and 14), privacy (section 3) and local models (section 2) passed\nModel management (section 2, increment 2): naming, pull/delete consent gates and offline honesty passed\nRemaining sections (5, 6, 8, 12, 18, 20 + extras 7, 9-11, 13): architecture, research, knowledge, goals, fullstack, smarthome, media and everyday passed\nCharts as image (section 19), RAM knowledge file index (section 8) and week plan passed\nRAG on fake embeddings (section 8): chunking, cosine ranking, prompt, offline honesty passed\nWiFi gate with SSRF guard (section 12) and Roblox game dev (Luau templates, DevEx math, web search) passed\nFull Roblox game (Mega Obby: 120 procedural stages, monetization, anti-cheat) and Blender generators verified passed\nGameForge genre generator (obby/simulator/tycoon/horror/shooter/racing from one GameSpec line) passed\nStudio 0.97: catalogue, staging, real runs, history and clearing passed\n");
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
    }
}
