using System.IO;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.ViewModels;

namespace SentinelX.Tests;

internal static class ModelPanelRegression
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("TEST FAILED: " + message); }
    private sealed class FakeAi : IAiService
    {
        public string RoutingReason => "test";
        public void Cancel() { }
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>(["qwen3:4b"]);
        public Task<string> SelectModelAsync(string model, CancellationToken token = default) => Task.FromResult(model);
        public Task<string> AskAsync(string input, string context, CancellationToken token) => throw new InvalidOperationException("Panel must not call AI");
    }
    private sealed class Engine : IActionEngine
    {
        public List<string> Calls { get; } = [];
        public bool IsStopped => false;
        public bool IsBusy => false;
        public ActionRecord? CurrentAction => null;
        public bool HasPendingPermission => false;
        public bool IsStreaming => false;
        public string PermissionSummary => "";
        public event Action<string>? StreamDelta { add { } remove { } }
        public event Action? Changed { add { } remove { } }
        public event Action<ActionRecord>? ActionStarted { add { } remove { } }
        public void Cancel() { }
        public void EmergencyStop() { }
        public void Resume() { }
        public Task<IntentResult> ExecuteAsync(string input, CancellationToken token = default, bool fromVoice = false, Action<string>? onDelta = null)
        { Calls.Add(input); return Task.FromResult(new IntentResult("Plan / wynik: " + input)); }
    }
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var engine = new Engine();
        var vm = new AiViewModel(new FakeAi(), engine);
        await vm.ManageCommand.ExecuteAsync("confirm");
        Check(engine.Calls.Count == 0, "confirmation without a plan does not dispatch");
        vm.ManagementModel = "qwen3:4b";
        await vm.ManageCommand.ExecuteAsync("pull");
        Check(engine.Calls.SequenceEqual(new[] { "model pobierz: qwen3:4b" }), "plan never confirms implicitly");
        vm.Confirmation = "tak";
        await vm.ManageCommand.ExecuteAsync("confirm");
        Check(engine.Calls.Count == 1, "inexact confirmation blocked");
        vm.Confirmation = vm.ExpectedConfirmation;
        await vm.ManageCommand.ExecuteAsync("confirm");
        Check(engine.Calls.Last() == "model pobierz: qwen3:4b potwierdzam" && vm.ExpectedConfirmation == "", "explicit confirmation routed once");
        await vm.ManageCommand.ExecuteAsync("confirm");
        Check(engine.Calls.Count == 2, "replay blocked by panel");
        await vm.ManageCommand.ExecuteAsync("delete");
        vm.ManagementModel = "gemma3:1b";
        Check(vm.ExpectedConfirmation == "" && vm.Confirmation == "", "model edit invalidates panel consent");
        await vm.ManageCommand.ExecuteAsync("info");
        Check(engine.Calls.Last() == "model info: gemma3:1b", "details go through central engine");
        await vm.ManageCommand.ExecuteAsync("running");
        Check(engine.Calls.Last() == "model uruchomione", "running models routed");
        await vm.RefreshCommand.ExecuteAsync(null);
        Check(vm.Models.Count == 1 && vm.SelectedModel == "qwen3:4b", "existing model selection preserved");
        File.WriteAllText(Path.Combine(directory, "model-panel.txt"), "PASS: plans, exact confirmation, replay, model change, central routing, list\n");
    }
}
