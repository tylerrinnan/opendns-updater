using System.Text.Json;

namespace OpenDnsUpdater;

public enum EventKind { Info, Success, Warning, Error }

/// <summary>One line of the in-app event log — a check that ran, or an update it produced.</summary>
public sealed class EventRecord
{
    public DateTimeOffset TimestampUtc { get; set; }
    public EventKind Kind { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>Bounded, persisted history of recent checks/updates, newest first, so the tray app
/// can show "last checks" across restarts without parsing the free-text log file. Mirrors
/// <see cref="AppSettingsStore"/>'s load/save pattern; best-effort like <see cref="AppLog"/> —
/// a history read/write failure must never crash the app or interrupt a check.</summary>
internal sealed class EventHistoryStore
{
    private const int MaxEntries = 200;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private List<EventRecord> _entries = new();

    public EventHistoryStore()
    {
        Load();
    }

    /// <summary>Newest-first snapshot of recent events, safe to enumerate off the storage lock.</summary>
    public IReadOnlyList<EventRecord> Snapshot()
    {
        lock (_gate) return _entries.ToList();
    }

    public void Record(EventKind kind, string message)
    {
        try
        {
            lock (_gate)
            {
                _entries.Insert(0, new EventRecord { TimestampUtc = DateTimeOffset.UtcNow, Kind = kind, Message = message });
                if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
                Save();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to record event history: {ex.Message}");
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(AppPaths.HistoryFile))
            {
                var json = File.ReadAllText(AppPaths.HistoryFile);
                _entries = JsonSerializer.Deserialize<List<EventRecord>>(json, JsonOptions) ?? new List<EventRecord>();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load event history, starting fresh: {ex.Message}");
            _entries = new List<EventRecord>();
        }
    }

    private void Save()
    {
        AppPaths.EnsureExists();
        var json = JsonSerializer.Serialize(_entries, JsonOptions);
        var tempFile = AppPaths.HistoryFile + ".tmp";
        File.WriteAllText(tempFile, json);
        File.Copy(tempFile, AppPaths.HistoryFile, overwrite: true);
        File.Delete(tempFile);
    }
}
