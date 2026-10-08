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
                    Registry.CurrentUser.CreateSubKey(
                        RegistryPath,
                        writable: true);


                if (key == null)
                    return false;


                // --autostart: when Windows starts us, stay in the tray instead of popping a window up at every login.
                string command =
                    $"\"{executablePath}\" --autostart";


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
                    return true;


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
        // AGENT ENTRY (headless --agent process, same user)
        // =========================================================

        public bool EnableAgent()
        {
            string? command =
                AgentCommand();

            if (command == null)
                return false;

            return SetEntry(
                applicationName + " Agent",
                command);
        }

        public bool DisableAgent() =>
            RemoveEntry(
                applicationName + " Agent");

        public bool IsAgentEnabled() =>
            GetEntry(
                applicationName + " Agent") != null;

        private static string? AgentCommand()
        {
            string? executablePath =
                Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(
                    executablePath))
            {
                return null;
            }

            return
                $"\"{executablePath}\" --agent";
        }

        private static bool SetEntry(
            string name,
            string command)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.CreateSubKey(
                        RegistryPath,
                        writable: true);

                if (key == null)
                    return false;

                key.SetValue(
                    name,
                    command,
                    RegistryValueKind.String);

                return string.Equals(
                    GetEntry(name),
                    command,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool RemoveEntry(
            string name)
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
                    return true;

                key.DeleteValue(
                    name,
                    throwOnMissingValue: false);

                return GetEntry(name) == null;
            }
            catch
            {
                return false;
            }
        }

        private static string? GetEntry(
            string name)
        {
            if (!OperatingSystem.IsWindows())
                return null;

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        RegistryPath,
                        writable: false);

                return key?.GetValue(name)?.ToString();
            }
            catch
            {
                return null;
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