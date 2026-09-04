namespace OpenDnsUpdater;

/// <summary>Minimal rolling text logger. Never throws — logging must not be able to crash the app.</summary>
internal static class AppLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureExists();
                RotateIfNeeded();
                File.AppendAllText(
                    AppPaths.LogFile,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(AppPaths.LogFile);
        if (file.Exists && file.Length > MaxBytes)
        {
            File.Copy(AppPaths.LogFile, AppPaths.LogFile + ".old", overwrite: true);
            File.WriteAllText(AppPaths.LogFile, string.Empty);
        }
    }
}
