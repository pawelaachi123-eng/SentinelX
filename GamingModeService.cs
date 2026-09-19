using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SentinelX;

/// <summary>Presence detection only; does not change Windows Game Mode or process priority.</summary>
public class GamingModeService
{
    private readonly object gate = new();
    private long sampledAt = long.MinValue;
    private string runningGame = string.Empty;
    private static readonly IReadOnlyDictionary<string, string> KnownGames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cs2"] = "Counter-Strike 2", ["dota2"] = "Dota 2", ["VALORANT-Win64-Shipping"] = "Valorant",
        ["League of Legends"] = "League of Legends", ["FortniteClient-Win64-Shipping"] = "Fortnite",
        ["r5apex"] = "Apex Legends", ["RocketLeague"] = "Rocket League", ["GTA5"] = "Grand Theft Auto V",
        ["Cyberpunk2077"] = "Cyberpunk 2077", ["eldenring"] = "Elden Ring", ["Overwatch"] = "Overwatch",
        ["Minecraft.Windows"] = "Minecraft", ["hl2"] = "Source game"
    };
    public bool DetectionAvailable { get; private set; } = true;
    public bool IsGaming() => !string.IsNullOrEmpty(GetRunningGame());

    public string GetRunningGame()
    {
        lock (gate)
        {
            long now = Environment.TickCount64;
            if (sampledAt != long.MinValue && now - sampledAt < 2000) return runningGame;
            sampledAt = now;
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try { if (KnownGames.TryGetValue(process.ProcessName, out var name)) found.Add(name); }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                    }
                }
                DetectionAvailable = true;
            }
            catch { DetectionAvailable = false; }
            runningGame = string.Join(", ", found.OrderBy(x => x));
            return runningGame;
        }
    }
}
