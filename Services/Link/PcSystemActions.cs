using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SentinelX.Services.Link;

internal interface IPcSystemActions
{
    bool IsSupported { get; }
    bool TryExecute(string action, out string message);
}

/// <summary>Small typed Windows power API. It accepts only three fixed operations; it never runs caller-provided text or arguments.</summary>
internal sealed class PcSystemActions : IPcSystemActions
{
    private const int ShutdownDelaySeconds = 30;
    private const string ShutdownComment = "Sentinel X: requested by a paired controller";

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool TryExecute(string action, out string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            message = "Zdalne zasilanie jest dostępne wyłącznie na komputerze z Windows.";
            return false;
        }

        try
        {
            switch (action)
            {
                case "lock":
                    if (!LockWorkStation())
                    {
                        message = "Windows odrzucił blokadę stacji roboczej.";
                        return false;
                    }
                    message = "Windows przyjął żądanie zablokowania stacji roboczej.";
                    return true;

                case "restart":
                case "shutdown":
                    string executable = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
                    var start = new ProcessStartInfo
                    {
                        FileName = executable,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Environment.SystemDirectory,
                    };
                    start.ArgumentList.Add(action == "restart" ? "/r" : "/s");
                    start.ArgumentList.Add("/t");
                    start.ArgumentList.Add(ShutdownDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    start.ArgumentList.Add("/c");
                    start.ArgumentList.Add(ShutdownComment);
                    using (Process? process = Process.Start(start))
                    {
                        if (process == null)
                        {
                            message = "Windows nie uruchomił polecenia zasilania.";
                            return false;
                        }
                        if (!process.WaitForExit(3000))
                        {
                            message = "Windows nie potwierdził przyjęcia polecenia w wymaganym czasie.";
                            return false;
                        }
                        if (process.ExitCode != 0)
                        {
                            message = "Windows odmówił polecenia zasilania (kod " + process.ExitCode + ").";
                            return false;
                        }
                    }
                    message = action == "restart"
                        ? "Windows przyjął żądanie ponownego uruchomienia za 30 sekund."
                        : "Windows przyjął żądanie wyłączenia za 30 sekund.";
                    return true;

                default:
                    message = "Nieobsługiwana operacja zasilania.";
                    return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            message = "Nie udało się zlecić operacji Windows: " + ex.Message;
            return false;
        }
    }
}
