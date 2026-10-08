using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SentinelX.Core;
using SentinelX.Services.Agent;

namespace SentinelX;

/// <summary>Windows: start the headless Agent in-process and prove the pipe contract.</summary>
public static class AgentSmokeRunner
{
    private static byte[] Unprotect(byte[] value) => ProtectedData.Unprotect(value, null, DataProtectionScope.CurrentUser);

    public static async Task<int> RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var lines = new List<string>();
        void Pass(string name) => lines.Add("PASS " + name);
        string root = AppPaths.Root;
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var cancel = budget.Token;
        AgentHost host = await AgentHost.StartAsync();
        try
        {
            using (var probe = new Mutex(false, AgentAuth.MutexName(root), out bool secondCreated))
                if (secondCreated) throw new InvalidOperationException("second agent instance allowed");
            Pass("single instance");
            string? token = AgentAuth.LoadToken(root, Unprotect);
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("token missing");
            Pass("token roundtrip");
            var client = new AgentClient(AgentAuth.PipeName(root), () => token);
            var status = await client.SendAsync("status", new { }, cancel);
            if (!status.TryGetProperty("status", out _) || !status.TryGetProperty("version", out _))
                throw new InvalidOperationException("status shape");
            Pass("pipe status");
            var tasks = await client.SendAsync("tasks", new { }, cancel);
            if (tasks.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("tasks shape");
            Pass("pipe tasks");
            await client.SendAsync("config", new { }, cancel);
            Pass("pipe config");
            try
            {
                await new AgentClient(AgentAuth.PipeName(root), () => new string('0', 64)).SendAsync("status", new { }, cancel);
                throw new InvalidOperationException("bad token accepted");
            }
            catch (AgentException e) when (e.Code == "auth") { Pass("auth reject"); }
            try
            {
                await client.SendAsync("no-such-op", new { }, cancel);
                throw new InvalidOperationException("unknown op accepted");
            }
            catch (AgentException e) when (e.Code == "unknown_op") { Pass("unknown op"); }
            string beat = AgentAuth.HeartbeatPath(root);
            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (!File.Exists(beat) && DateTime.UtcNow < deadline) await Task.Delay(500, cancel);
            using (var beatDoc = JsonDocument.Parse(await File.ReadAllTextAsync(beat, cancel)))
            {
                var b = beatDoc.RootElement;
                if (!b.TryGetProperty("pid", out _) || !b.TryGetProperty("time", out _) || !b.TryGetProperty("metrics", out _))
                    throw new InvalidOperationException("heartbeat shape");
            }
            Pass("heartbeat");
            var report = await client.SendAsync("diagnose", new { }, cancel);
            if (report.ValueKind != JsonValueKind.Array || report.GetArrayLength() < 10)
                throw new InvalidOperationException("diagnose count");
            Pass("diagnose");
            var stop = await client.SendAsync("stop", new { }, cancel);
            if (!stop.TryGetProperty("stopping", out _)) throw new InvalidOperationException("stop shape");
            var stopDeadline = DateTime.UtcNow.AddSeconds(30);
            bool released = false;
            while (DateTime.UtcNow < stopDeadline)
            {
                using (var probe = new Mutex(true, AgentAuth.MutexName(root), out bool free))
                {
                    if (free)
                    {
                        probe.ReleaseMutex();
                        released = true;
                        break;
                    }
                }
                await Task.Delay(500, cancel);
            }
            if (!released) throw new InvalidOperationException("agent did not stop");
            Pass("stop");
            lines.Add("AGENT SMOKE PASS");
            await File.WriteAllTextAsync(Path.Combine(output, "AGENT-SMOKE.txt"), string.Join(Environment.NewLine, lines) + Environment.NewLine, cancel);
            return 0;
        }
        catch (Exception e)
        {
            try { await File.WriteAllTextAsync(Path.Combine(output, "FAILED.txt"), e.ToString(), CancellationToken.None); } catch { }
            return 1;
        }
        finally
        {
            await host.DisposeAsync();
        }
    }
}
