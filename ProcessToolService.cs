using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SentinelX
{
    public sealed class ProcessToolService
    {
        private static readonly Dictionary<string, string[]> AllowedApps =
            new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["discord"] =
                    new[]
                    {
                        "Discord"
                    },

                ["steam"] =
                    new[]
                    {
                        "steam",
                        "steamwebhelper"
                    },

                ["cs2"] =
                    new[]
                    {
                        "cs2"
                    },

                ["brave"] =
                    new[]
                    {
                        "brave"
                    },

                ["chrome"] =
                    new[]
                    {
                        "chrome"
                    },

                ["notatnik"] =
                    new[]
                    {
                        "notepad"
                    },

                ["notepad"] =
                    new[]
                    {
                        "notepad"
                    },

                ["spotify"] =
                    new[]
                    {
                        "Spotify"
                    }
            };


        public string GetTopMemoryProcesses(
            int count = 10)
        {
            List<(string Name, int Id, long Memory)>
                data =
                    new List<(string, int, long)>();


            foreach (
                Process process
                in Process.GetProcesses())
            {
                try
                {
                    data.Add(
                        (
                            process.ProcessName,
                            process.Id,
                            process.WorkingSet64
                        ));
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }


            StringBuilder result =
                new StringBuilder();


            result.AppendLine(
                "TOP PROCESY WG RAM");


            result.AppendLine();


            foreach (
                var process
                in data
                    .OrderByDescending(
                        x => x.Memory)
                    .Take(count))
            {
                double memoryMb =
                    process.Memory /
                    1024d /
                    1024d;


                result.AppendLine(
                    $"{process.Name}  •  {memoryMb:0} MB  •  PID {process.Id}");
            }


            return result
                .ToString()
                .Trim();
        }


        public async Task<ActionExecutionResult>
            CloseAppAsync(
                string target)
        {
            string normalized = AppLauncherService.CanonicalizeLaunchTarget(target);


            if (!AllowedApps.TryGetValue(
                    normalized,
                    out string[]? processNames))
            {
                return ActionExecutionResult.Failure(
                    $"Zamykanie „{target}” nie jest dostępne w bezpiecznej liście.",
                    "Sentinel nie zamyka dowolnych procesów systemowych.");
            }


            List<Process> processes =
                new List<Process>();


            foreach (string processName
                     in processNames)
            {
                try
                {
                    processes.AddRange(
                        Process.GetProcessesByName(
                            processName));
                }
                catch
                {
                }
            }


            if (processes.Count ==
                0)
            {
                return ActionExecutionResult.VerifiedSuccess(
                    $"{target} już nie działa.",
                    "Nie znaleziono procesu przed wykonaniem akcji.");
            }


            int affected =
                0;


            foreach (Process process
                     in processes)
            {
                try
                {
                    bool requestedClose =
                        process.CloseMainWindow();


                    if (requestedClose)
                    {
                        affected++;
                    }
                }
                catch
                {
                }
            }


            await Task.Delay(
                1600);


            foreach (Process process
                     in processes)
            {
                try
                {
                    if (process.HasExited)
                        continue;


                    // A save dialog may be open. Do not force-kill the process.
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }


            await Task.Delay(
                500);


            bool stillRunning =
                false;


            foreach (string processName
                     in processNames)
            {
                try
                {
                    Process[] remaining =
                        Process.GetProcessesByName(
                            processName);


                    if (remaining.Length >
                        0)
                    {
                        stillRunning =
                            true;
                    }


                    foreach (Process process
                             in remaining)
                    {
                        process.Dispose();
                    }
                }
                catch
                {
                }
            }


            if (!stillRunning)
            {
                return ActionExecutionResult.VerifiedSuccess(
                    $"Zamknięto {target}.",
                    $"Po akcji nie wykryto procesów aplikacji. Próby zamknięcia: {affected}.");
            }


            return ActionExecutionResult.Failure(
                $"{target} nadal działa po żądaniu zamknięcia.",
                "Sprawdź okno aplikacji i ewentualny dialog zapisu. Nie wymuszono zakończenia procesu.");
        }


        private static string Normalize(
            string text)
        {
            return text
                .Trim()
                .ToLowerInvariant()
                .Replace('ą', 'a')
                .Replace('ć', 'c')
                .Replace('ę', 'e')
                .Replace('ł', 'l')
                .Replace('ń', 'n')
                .Replace('ó', 'o')
                .Replace('ś', 's')
                .Replace('ż', 'z')
                .Replace('ź', 'z');
        }
    }
}
