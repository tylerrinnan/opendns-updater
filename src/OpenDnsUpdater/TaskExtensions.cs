namespace OpenDnsUpdater;

internal static class TaskExtensions
{
    /// <summary>Observes a fire-and-forget Task's exceptions by logging them, instead of
    /// letting them vanish as an unobserved task exception (which .NET doesn't crash on —
    /// it just disappears silently, which makes real bugs invisible).</summary>
    public static async void FireAndForget(this Task task, string context)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Unhandled exception in {context}: {ex}");
        }
    }
}
