using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace SentinelX;

public sealed record DesktopTarget(IntPtr Handle, int ProcessId, string ProcessName, string Title, DateTimeOffset SeenAt);
public sealed record DesktopControlCandidate(string Name, string ControlType, string AutomationId, int[] RuntimeId);

/// <summary>
/// Read-only UI Automation over the last foreground non-Sentinel window. Button invocation is separate,
/// revalidates the target and is intended to run only after PermissionCenter approval.
/// This is not screenshot vision and never reads editable control values.
/// </summary>
public sealed class DesktopAutomationTool
{
    private const int MaxVisibleControls = 80;
    private static readonly TimeSpan TargetLifetime = TimeSpan.FromMinutes(5);
    private readonly object gate = new();
    private readonly Func<bool> externalNetworkAllowed;
    public DesktopAutomationTool(Func<bool>? externalNetworkAllowed = null) => this.externalNetworkAllowed = externalNetworkAllowed ?? (() => true);
    private DesktopTarget? lastTarget;
    private DesktopTarget? pendingSelectionTarget;
    private IReadOnlyList<DesktopControlCandidate> pendingCandidates = [];

    public void SetPendingCandidates(DesktopTarget target, IReadOnlyList<DesktopControlCandidate> candidates)
    {
        lock (gate) { pendingSelectionTarget = target; pendingCandidates = candidates.ToArray(); }
    }

    public bool TryTakePendingCandidate(int oneBasedIndex, out DesktopTarget? target, out DesktopControlCandidate? candidate, out int choiceCount)
    {
        lock (gate)
        {
            target = pendingSelectionTarget;
            int index = oneBasedIndex - 1;
            choiceCount = pendingCandidates.Count;
            if (target == null || index < 0 || index >= pendingCandidates.Count)
            { candidate = null; return false; }
            candidate = pendingCandidates[index];
            pendingSelectionTarget = null; pendingCandidates = [];
            return true;
        }
    }

    public void ClearPendingCandidates()
    { lock (gate) { pendingSelectionTarget = null; pendingCandidates = []; } }

    public DesktopTarget? GetPendingSelectionTarget()
    { lock (gate) return pendingSelectionTarget; }

    public void RememberForegroundWindow()
    {
        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !IsWindowVisible(handle)) return;
        _ = GetWindowThreadProcessId(handle, out uint rawPid);
        int pid = unchecked((int)rawPid);
        if (pid <= 0 || pid == Environment.ProcessId) return;
        try
        {
            using Process process = Process.GetProcessById(pid);
            var title = new StringBuilder(512);
            _ = GetWindowText(handle, title, title.Capacity);
            lock (gate) lastTarget = new(handle, pid, process.ProcessName, title.ToString(), DateTimeOffset.Now);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public string ReadVisibleControls()
    {
        DesktopTarget? target = GetFreshTarget();
        if (target == null) return "Nie mam świeżego kontekstu okna spoza Sentinela. Uaktywnij docelową aplikację i spróbuj ponownie.";
        if (!TryGetRoot(target, out AutomationElement? root, out string error)) return error;
        try
        {
            var controls = GetVisibleNamedControls(root!).Take(MaxVisibleControls).ToArray();
            if (controls.Length == 0) return $"Okno {Describe(target)} nie udostępnia tekstowych kontrolek UI Automation. Nie wykonano OCR ani analizy obrazu.";
            var output = new StringBuilder($"Tekstowe kontrolki dostępne przez Windows UI Automation w {Describe(target)} (maks. {MaxVisibleControls}; bez odczytu wartości pól):\n");
            foreach (AutomationElement element in controls)
            {
                try
                {
                    var current = element.Current;
                    string name = SensitiveDataRedactor.Redact(Limit(current.Name, 160));
                    if (name.Length == 0) continue;
                    output.Append("• ").Append(current.ControlType.ProgrammaticName.Replace("ControlType.", "", StringComparison.Ordinal))
                        .Append(": ").Append(name);
                    if (!current.IsEnabled) output.Append(" [wyłączone]");
                    output.AppendLine();
                }
                catch (ElementNotAvailableException) { }
                if (output.Length > 12_000) { output.AppendLine("… [ucięto] "); break; }
            }
            return output.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        { return "Nie udało się odczytać drzewa UI Automation: " + ex.Message; }
    }

    public IReadOnlyList<DesktopControlCandidate> FindClickableControls(string exactName, out string error)
    {
        error = string.Empty;
        DesktopTarget? target = GetFreshTarget();
        if (target == null) { error = "Nie mam świeżego kontekstu okna docelowego."; return []; }
        if (!TryGetRoot(target, out AutomationElement? root, out error)) return [];
        string wanted = ConversationMemoryService.Normalize(exactName).Trim();
        var matches = new List<DesktopControlCandidate>();
        try
        {
            foreach (AutomationElement element in GetVisibleNamedControls(root!))
            {
                try
                {
                    var current = element.Current;
                    if (!current.IsEnabled || current.IsOffscreen ||
                        current.ControlType != ControlType.Button && current.ControlType != ControlType.Hyperlink ||
                        ConversationMemoryService.Normalize(current.Name).Trim() != wanted) continue;
                    matches.Add(new(current.Name, current.ControlType.ProgrammaticName, Limit(current.AutomationId, 80), element.GetRuntimeId()));
                    if (matches.Count >= 10) break;
                }
                catch (ElementNotAvailableException) { }
            }
            return matches;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException)
        { error = "Nie udało się przeszukać kontrolek UI Automation: " + ex.Message; return []; }
    }

    public ActionExecutionResult InvokeButtonAfterApproval(DesktopTarget expectedTarget, DesktopControlCandidate candidate)
    {
        if (!externalNetworkAllowed()) return ActionExecutionResult.Failure("Tryb tylko lokalnie zablokował sterowanie oknem innej aplikacji.", "Nie wysłano Invoke do kontrolki.");
        DesktopTarget? currentTarget = GetFreshTarget();
        if (currentTarget == null || currentTarget.Handle != expectedTarget.Handle || currentTarget.ProcessId != expectedTarget.ProcessId)
            return ActionExecutionResult.Failure("Okno docelowe zmieniło się lub wygasło. Nie kliknięto kontrolki.");
        if (!IsWindow(currentTarget.Handle)) return ActionExecutionResult.Failure("Okno docelowe nie jest już dostępne.");
        if (!TryGetRoot(currentTarget, out AutomationElement? root, out string error))
            return ActionExecutionResult.Failure("Okno docelowe nie jest już dostępne.", error);
        try
        {
            AutomationElement[] matches = GetVisibleNamedControls(root!).Where(element =>
            {
                try
                {
                    var item = element.Current;
                    return item.IsEnabled && !item.IsOffscreen &&
                        (item.ControlType == ControlType.Button || item.ControlType == ControlType.Hyperlink) &&
                        item.ControlType.ProgrammaticName == candidate.ControlType && item.AutomationId == candidate.AutomationId &&
                        ConversationMemoryService.Normalize(item.Name).Trim() == ConversationMemoryService.Normalize(candidate.Name).Trim() &&
                        element.GetRuntimeId().SequenceEqual(candidate.RuntimeId);
                }
                catch (ElementNotAvailableException) { return false; }
            }).Take(2).ToArray();
            if (matches.Length != 1) return ActionExecutionResult.Failure("Kontrolka nie jest już jednoznaczna lub widoczna. Nie kliknięto.");
            if (!matches[0].TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
                return ActionExecutionResult.Failure("Ta kontrolka nie udostępnia bezpiecznego wzorca Invoke. Nie kliknięto.");
            ((InvokePattern)pattern).Invoke();
            return ActionExecutionResult.UnverifiedSuccess("Wysłano Invoke do wskazanego elementu UI. Nie potwierdzono skutku w aplikacji.",
                $"Okno: {Describe(currentTarget)}\nKontrolka: {candidate.ControlType} „{SensitiveDataRedactor.Redact(candidate.Name)}”.");
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        { return ActionExecutionResult.Failure("Nie udało się bezpiecznie wywołać kontrolki.", ex.Message); }
    }

    public DesktopTarget? GetFreshTarget()
    {
        lock (gate)
        {
            if (lastTarget == null || DateTimeOffset.Now - lastTarget.SeenAt > TargetLifetime) return null;
            if (!IsWindow(lastTarget.Handle)) return null;
            _ = GetWindowThreadProcessId(lastTarget.Handle, out uint pid);
            return unchecked((int)pid) == lastTarget.ProcessId ? lastTarget : null;
        }
    }

    private static bool TryGetRoot(DesktopTarget target, out AutomationElement? root, out string error)
    {
        root = null; error = string.Empty;
        try
        {
            root = AutomationElement.FromHandle(target.Handle);
            if (root != null) return true;
            error = "UI Automation zwróciło puste drzewo okna.";
            return false;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException)
        { error = "Nie można odczytać UI Automation okna „" + Describe(target) + "”: " + ex.Message; return false; }
    }

    private static IEnumerable<AutomationElement> GetVisibleNamedControls(AutomationElement root)
    {
        var visible = new PropertyCondition(AutomationElement.IsOffscreenProperty, false);
        var named = new NotCondition(new PropertyCondition(AutomationElement.NameProperty, string.Empty));
        var condition = new AndCondition(visible, named);
        AutomationElementCollection items = root.FindAll(TreeScope.Descendants, condition);
        int count = Math.Min(items.Count, 400);
        for (int i = 0; i < count; i++) yield return items[i];
    }

    private static string Describe(DesktopTarget target) =>
        target.Title.Length == 0 ? target.ProcessName : target.ProcessName + " — " + SensitiveDataRedactor.Redact(Limit(target.Title, 100));
    private static string Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? "" : value.Length <= max ? value : value[..max] + "…";

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
}
