using Microsoft.Win32;

namespace Luminosity.Services;

/// <summary>Manages the HKCU "Run" entry so Luminosity can launch (minimized) at login.</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Luminosity";

    private static string ExePath => Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (key is null) return;

        if (enabled)
            key.SetValue(ValueName, $"\"{ExePath}\" --minimized");
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
