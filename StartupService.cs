using Microsoft.Win32;
using System;

namespace SentinelX
{
    public class StartupService
    {
        private const string RegistryPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly string applicationName;


        // =========================================================
        // CONSTRUCTOR
        // =========================================================

        public StartupService(
            string applicationName)
        {
            this.applicationName =
                applicationName;
        }


        // =========================================================
        // ENABLE
        // =========================================================

        public bool Enable()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                string? executablePath =
                    Environment.ProcessPath;

                if (string.IsNullOrWhiteSpace(
                        executablePath))
                {
                    return false;
                }


                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        RegistryPath,
                        writable: true);


                if (key == null)
                    return false;


                string command =
                    $"\"{executablePath}\"";


                key.SetValue(
                    applicationName,
                    command,
                    RegistryValueKind.String);


                // No Proof = No Success
                return VerifyEnabled(
                    command);
            }
            catch
            {
                return false;
            }
        }


        // =========================================================
        // DISABLE
        // =========================================================

        public bool Disable()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        RegistryPath,
                        writable: true);


                if (key == null)
                    return false;


                key.DeleteValue(
                    applicationName,
                    throwOnMissingValue: false);


                // Verification
                return !IsEnabled();
            }
            catch
            {
                return false;
            }
        }


        // =========================================================
        // STATUS
        // =========================================================

        public bool IsEnabled()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        RegistryPath,
                        writable: false);


                if (key == null)
                    return false;


                object? value =
                    key.GetValue(
                        applicationName);


                return value != null &&
                       !string.IsNullOrWhiteSpace(
                           value.ToString());
            }
            catch
            {
                return false;
            }
        }


        // =========================================================
        // VERIFY
        // =========================================================

        private bool VerifyEnabled(
            string expectedCommand)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        RegistryPath,
                        writable: false);


                if (key == null)
                    return false;


                string? actual =
                    key.GetValue(
                            applicationName)
                        ?.ToString();


                return string.Equals(
                    actual,
                    expectedCommand,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}