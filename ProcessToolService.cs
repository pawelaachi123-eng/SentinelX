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


        /// <summary>Read-only detection of visible GUI processes that currently do not respond.</summary>
        public IReadOnlyList<(string Name, int ProcessId)> GetUnresponsiveApps(int limit = 10)
        {
            var result = new List<(string Name, int ProcessId)>();
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && !process.Responding)
                        result.Add((process.ProcessName, process.Id));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                finally { process.Dispose(); }
                if (result.Count >= Math.Clamp(limit, 1, 50)) break;
            }
            return result;
        }

        public bool CanCloseSafely(string target) => AllowedApps.ContainsKey(AppLauncherService.CanonicalizeLaunchTarget(target));

        public async Task<ActionExecutionResult>
            CloseAppAsync(
                string target, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                        !cancellationToken.IsCancellationRequested && process.CloseMainWindow();


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


            cancellationToken.ThrowIfCancellationRequested();
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
