namespace OpenDnsUpdater;

internal static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenDnsUpdater");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LogFile => Path.Combine(DataDirectory, "log.txt");
    public static string HistoryFile => Path.Combine(DataDirectory, "history.json");

    public static void EnsureExists() => Directory.CreateDirectory(DataDirectory);
}
