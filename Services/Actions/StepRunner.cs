namespace SentinelX.Services.Actions;

/// <summary>Wynik jednego kroku wykonanego przez pełen potok (IntentRouter → serwisy → dowody).</summary>
public sealed record StepOutcome(bool Success, bool Verified, string Text, string ActionId, string Status)
{
    public static StepOutcome Unavailable(string reason) => new(false, false, reason, "", "UNAVAILABLE");
    public bool HasProof => Verified;
}

/// <summary>
/// Wykonanie kroku przez <see cref="IActionEngine"/>, a nie przez bezpośrednie wołanie routera.
/// Dzięki temu każdy krok agenta i każdy krok sekwencji dostaje własny requestId, własny zakres
/// zgody (ApprovalContext) i własny audyt — a STOP awaryjny blokuje je tak samo jak wpisywane polecenia.
///
/// Właśnie dlatego jest to osobny, leniwie podłączany serwis: silnik zależy od routera, a router
/// (przez Jarvis) od wykonawcy kroków. Cykl DI został przerwany tu, a nie w konstruktorze:
/// <see cref="Attach"/> woła kompozycja (MainViewModel), więc bez podłączenia agent i sekwencje
/// odpowiadają uczciwie „nie jestem podłączony”, zamiast rzucić wyjątkiem przy starcie aplikacji.
/// </summary>
public sealed class StepRunner
{
    private volatile Func<string, CancellationToken, Task<StepOutcome>>? runner;

    public bool IsReady => runner != null;

    /// <summary>Podłącza wykonawcę kroków. Wołane raz, przy starcie powłoki.</summary>
    public void Attach(Func<string, CancellationToken, Task<StepOutcome>> executor) => runner = executor ?? throw new ArgumentNullException(nameof(executor));

    public async Task<StepOutcome> RunAsync(string command, CancellationToken token)
    {
        Func<string, CancellationToken, Task<StepOutcome>>? current = runner;
        if (current == null)
            return StepOutcome.Unavailable("Wykonawca kroków nie jest podłączony — agent i sekwencje działają w głównym oknie Sentinel.");
        return await current(command, token);
    }
}
