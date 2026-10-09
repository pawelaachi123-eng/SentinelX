using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SentinelX.Services.Agent;

/// <summary>Per-data-directory Agent identity: pipe/mutex names and the boot token.
/// Protection delegates are injectable so contracts can run without Windows DPAPI.</summary>
public static class AgentAuth
{
    public static string Key(string root) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..24];
    public static string PipeName(string root) => "SentinelX-Agent-" + Key(root);
    public static string MutexName(string root) => @"Local\SentinelX-Agent-" + Key(root);
    public static string OwnerMutexName(string root) => @"Local\SentinelX-BaseOwner-" + Key(root);
    public static string AgentDirectory(string root) => Path.Combine(root, "Agent");
    public static string TokenPath(string root) => Path.Combine(AgentDirectory(root), "token.bin");
    public static string HeartbeatPath(string root) => Path.Combine(AgentDirectory(root), "heartbeat.json");

    public static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static void SaveToken(string root, string token, Func<byte[], byte[]> protect)
    {
        if (string.IsNullOrEmpty(token)) throw new ArgumentException("token");
        Directory.CreateDirectory(AgentDirectory(root));
        string path = TokenPath(root), temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, protect(Encoding.UTF8.GetBytes(token)));
            File.Move(temp, path, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    public static string? LoadToken(string root, Func<byte[], byte[]> unprotect)
    {
        try
        {
            string path = TokenPath(root);
            if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 4096) return null;
            string token = Encoding.UTF8.GetString(unprotect(File.ReadAllBytes(path)));
            return token.Length == 64 ? token : null;
        }
        catch { return null; }
    }

    public static bool ValidToken(string? candidate, string? expected)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(expected)) return false;
        byte[] a = Encoding.UTF8.GetBytes(candidate), b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Single Base-loop owner across UI/agent processes. The winner runs
    /// the connection and queue loops; the loser serves state but starts nothing.
    /// A mutex abandoned by a crashed owner counts as acquired.</summary>
    public static Mutex? TryOwnBase(string root)
    {
        var mutex = new Mutex(false, OwnerMutexName(root));
        try
        {
            if (mutex.WaitOne(0)) return mutex;
        }
        catch (AbandonedMutexException) { return mutex; }
        mutex.Dispose();
        return null;
    }
}
