using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDnsUpdater;

/// <summary>Persisted configuration. The password is never stored in plain text —
/// it's encrypted with Windows DPAPI, scoped to the current Windows user account,
/// so the settings file is useless if copied elsewhere or read by another account.</summary>
public sealed class AppSettings
{
    public string Email { get; set; } = "";
    public string NetworkLabel { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
    public int PollIntervalMinutes { get; set; } = 5;
    public bool StartWithWindows { get; set; } = true;
    public bool NotifyOnSuccess { get; set; } = true;
    public string? LastKnownIp { get; set; }
    public DateTimeOffset? LastUpdateUtc { get; set; }
    public string? LastResult { get; set; }

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OpenDnsUpdater.settings.v1");

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(NetworkLabel) &&
        !string.IsNullOrWhiteSpace(EncryptedPassword);

    public void SetPassword(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            EncryptedPassword = "";
            return;
        }

        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
        EncryptedPassword = Convert.ToBase64String(protectedBytes);
    }

    public string? GetPassword()
    {
        if (string.IsNullOrEmpty(EncryptedPassword)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(EncryptedPassword), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            // Different Windows account, or the blob is corrupt — treat as unset.
            return null;
        }
    }
}

/// <summary>Loads/saves <see cref="AppSettings"/> to a JSON file under %LOCALAPPDATA%.</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();

    public AppSettings Current { get; private set; } = new();

    public AppSettingsStore()
    {
        AppPaths.EnsureExists();
        Load();
    }

    public void Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    var json = File.ReadAllText(AppPaths.SettingsFile);
                    Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to load settings, starting fresh: {ex.Message}");
                Current = new AppSettings();
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            AppPaths.EnsureExists();
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            var tempFile = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Copy(tempFile, AppPaths.SettingsFile, overwrite: true);
            File.Delete(tempFile);
        }
    }
}
