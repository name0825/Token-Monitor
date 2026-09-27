using System.IO;
using System.Security;
using Microsoft.Win32;

namespace TokenMonitor.App;

public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TokenMonitor";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && MatchesCurrentProcess(value);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool MatchesCurrentProcess(string value)
    {
        string? processPath = Environment.ProcessPath;
        return processPath is not null
            && string.Equals(value, "\"" + processPath + "\"", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (!enabled)
            {
                if (key.GetValue(ValueName) is string value && MatchesCurrentProcess(value))
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                return;
            }

            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                return;
            }

            key.SetValue(ValueName, "\"" + processPath + "\"", RegistryValueKind.String);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
        }
    }
}
