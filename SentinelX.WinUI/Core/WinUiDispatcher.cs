using Microsoft.UI.Dispatching;

namespace SentinelX.Core;

/// <summary>
/// UI-thread dispatcher contract. Same fully-qualified name as the WPF build,
/// so every shared ViewModel and service compiles unchanged against WinUI.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>WinUI 3 implementation over <see cref="DispatcherQueue"/>.</summary>
public sealed class WinUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public void Post(Action action)
    {
        if (queue.HasThreadAccess)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }

            return;
        }

        queue.TryEnqueue(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }
        });
    }
}
