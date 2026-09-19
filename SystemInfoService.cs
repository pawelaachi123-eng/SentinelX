using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SentinelX
{
    public class SystemInfoService
    {
        // =========================================================
        // CPU
        // =========================================================

        public string GetCpuName()
        {
            if (!OperatingSystem.IsWindows())
            {
                return
                    Environment.GetEnvironmentVariable(
                        "PROCESSOR_IDENTIFIER")
                    ?? "Unknown CPU";
            }


            try
            {
                using RegistryKey? key =
                    Registry.LocalMachine.OpenSubKey(
                        @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");


                string? processorName =
                    key?.GetValue(
                            "ProcessorNameString")
                        ?.ToString()
                        ?.Trim();


                if (!string.IsNullOrWhiteSpace(
                        processorName))
                {
                    return processorName;
                }
            }
            catch
            {
            }


            return
                Environment.GetEnvironmentVariable(
                    "PROCESSOR_IDENTIFIER")
                ?? "Unknown CPU";
        }


        // =========================================================
        // WINDOWS
        // =========================================================

        public string GetWindowsVersion()
        {
            if (!OperatingSystem.IsWindows())
            {
                return
                    RuntimeInformation.OSDescription;
            }


            try
            {
                using RegistryKey? key =
                    Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");


                if (key == null)
                {
                    return
                        RuntimeInformation.OSDescription;
                }


                string productName =
                    key.GetValue(
                            "ProductName")
                        ?.ToString()
                    ?? "Windows";


                string displayVersion =
                    key.GetValue(
                            "DisplayVersion")
                        ?.ToString()
                    ?? key.GetValue(
                            "ReleaseId")
                        ?.ToString()
                    ?? string.Empty;


                string build =
                    key.GetValue(
                            "CurrentBuildNumber")
                        ?.ToString()
                    ?? Environment.OSVersion.Version.Build
                        .ToString();


                string ubr =
                    key.GetValue(
                            "UBR")
                        ?.ToString()
                    ?? string.Empty;


                if (int.TryParse(
                        build,
                        out int buildNumber) &&
                    buildNumber >= 22000 &&
                    productName.Contains(
                        "Windows 10",
                        StringComparison.OrdinalIgnoreCase))
                {
                    productName =
                        productName.Replace(
                            "Windows 10",
                            "Windows 11",
                            StringComparison.OrdinalIgnoreCase);
                }


                string fullBuild =
                    string.IsNullOrWhiteSpace(
                        ubr)
                        ? build
                        : $"{build}.{ubr}";


                if (!string.IsNullOrWhiteSpace(
                        displayVersion))
                {
                    return
                        $"{productName} {displayVersion} • Build {fullBuild}";
                }


                return
                    $"{productName} • Build {fullBuild}";
            }
            catch
            {
                return
                    RuntimeInformation.OSDescription;
            }
        }


        // =========================================================
        // UPTIME
        // =========================================================

        public string GetUptime()
        {
            try
            {
                TimeSpan uptime =
                    TimeSpan.FromMilliseconds(
                        Environment.TickCount64);


                if (uptime.TotalDays >= 1)
                {
                    return
                        $"{(int)uptime.TotalDays}d {uptime.Hours}h";
                }


                if (uptime.TotalHours >= 1)
                {
                    return
                        $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
                }


                return
                    $"{uptime.Minutes}m";
            }
            catch
            {
                return "N/A";
            }
        }


        // =========================================================
        // DISKS
        // =========================================================

        public string GetDiskInfo()
        {
            try
            {
                DriveInfo[] drives =
                    DriveInfo.GetDrives()
                        .Where(
                            drive =>
                                drive.DriveType ==
                                DriveType.Fixed)
                        .Where(
                            drive =>
                            {
                                try
                                {
                                    return drive.IsReady;
                                }
                                catch
                                {
                                    return false;
                                }
                            })
                        .ToArray();


                if (drives.Length == 0)
                {
                    return
                        "No fixed drives detected.";
                }


                return string.Join(
                    Environment.NewLine,
                    drives.Select(
                        drive =>
                        {
                            try
                            {
                                double totalGb =
                                    drive.TotalSize /
                                    1024d /
                                    1024d /
                                    1024d;


                                double freeGb =
                                    drive.AvailableFreeSpace /
                                    1024d /
                                    1024d /
                                    1024d;


                                double usedGb =
                                    totalGb -
                                    freeGb;


                                return
                                    $"{drive.Name} {usedGb:0.0} / {totalGb:0.0} GB";
                            }
                            catch
                            {
                                return
                                    $"{drive.Name} unavailable";
                            }
                        }));
            }
            catch
            {
                return
                    "Disk information unavailable.";
            }
        }
    }
}