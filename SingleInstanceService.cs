using System.Security.Cryptography;
using System.Text;

namespace SentinelX;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private RegisteredWaitHandle? listener;
    public bool IsPrimary { get; }
    public SingleInstanceService(string identity)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        mutex = new Mutex(true, @"Local\SentinelX-" + key, out bool created);
        IsPrimary = created;
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SentinelX-Activate-" + key);
    }
    public void Listen(Action activate) => listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, timedOut) => { if (!timedOut) activate(); }, null, Timeout.Infinite, false);
    public void ActivateExisting() => activation.Set();
    public void Dispose()
    {
        listener?.Unregister(null); activation.Dispose();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
