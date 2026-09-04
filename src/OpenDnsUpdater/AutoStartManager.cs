using System.Diagnostics;
using Microsoft.Win32;

namespace OpenDnsUpdater;

/// <summary>Registers/removes a per-user auto-start entry. Uses the standard
/// HKCU Run key — no admin rights required, and it only affects the current user.</summary>
internal static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenDnsUpdater";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string existing &&
               existing.Equals(GetExePathQuoted(), StringComparison.OrdinalIgnoreCase);
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, GetExePathQuoted());
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string GetExePathQuoted()
    {
        // Assembly.Location is not usable here: it returns "" for a single-file publish.
        // ProcessPath/MainModule.FileName both report the actual host exe in every publish mode.
        var path = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not determine the running executable's path.");
        return $"\"{path}\"";
    }
}
