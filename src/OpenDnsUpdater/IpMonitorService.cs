using System.Net.NetworkInformation;

namespace OpenDnsUpdater;

/// <summary>
/// Watches for IP changes and pushes updates to OpenDNS when needed.
///
/// Detection is event-driven first: Windows raises NetworkAddressChanged whenever a
/// local adapter's configuration changes (DHCP renewal, Wi-Fi reconnect, VPN up/down),
/// which is what actually causes most public-IP changes — so we react to that
/// immediately instead of polling for it. A slow periodic poll runs alongside it purely
/// as a safety net, since an ISP can occasionally renumber you without any local
/// adapter event firing.
///
/// A single reschedulable timer drives both paths, so there is never more than one
/// check in flight and never more than one timer alive.
/// </summary>
internal sealed class IpMonitorService : IDisposable
{
    private readonly AppSettingsStore _settingsStore;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private System.Threading.Timer? _timer;
    private bool _started;

    public event Action<string>? StatusChanged;
    public event Action<OpenDnsUpdateResult, string>? UpdateCompleted;

    public IpMonitorService(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        ScheduleNext(TimeSpan.FromSeconds(5)); // brief delay so the network settles right after app launch/boot
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        _timer?.Dispose();
        _timer = null;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Debounce();

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable) Debounce();
    }

    private void Debounce() => ScheduleNext(TimeSpan.FromSeconds(8)); // let DHCP/link state settle before checking

    private void ScheduleNext(TimeSpan due)
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => CheckAndUpdateAsync().FireAndForget(nameof(CheckAndUpdateAsync)), null, due, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Runs one check-and-update cycle now. Safe to call concurrently — a
    /// second call while one is already running is a no-op.</summary>
    public async Task CheckAndUpdateAsync()
    {
        if (!await _runGate.WaitAsync(0)) return;
        bool failed = false;
        try
        {
            var settings = _settingsStore.Current;

            var ip = await PublicIpResolver.GetPublicIpAsync(CancellationToken.None);
            if (ip is null)
            {
                failed = true;
                StatusChanged?.Invoke("Couldn't determine public IP address; will retry.");
                return;
            }

            var ipText = ip.ToString();
            var alreadyCurrent = ipText == settings.LastKnownIp && settings.LastResult == nameof(OpenDnsUpdateStatus.Updated);
            if (alreadyCurrent)
            {
                StatusChanged?.Invoke($"No change ({ipText}).");
                return;
            }

            if (!settings.IsConfigured)
            {
                StatusChanged?.Invoke("Not configured yet — open Settings from the tray icon.");
                return;
            }

            var password = settings.GetPassword();
            if (password is null)
            {
                failed = true;
                StatusChanged?.Invoke("Saved password couldn't be read — please re-enter it in Settings.");
                return;
            }

            var result = await OpenDnsClient.UpdateAsync(settings.Email, password, settings.NetworkLabel, ip, CancellationToken.None);

            settings.LastKnownIp = ipText;
            settings.LastUpdateUtc = DateTimeOffset.UtcNow;
            settings.LastResult = result.Status.ToString();
            _settingsStore.Save();

            failed = !result.IsSuccess;
            UpdateCompleted?.Invoke(result, ipText);
        }
        finally
        {
            _runGate.Release();
            if (_started)
            {
                var interval = TimeSpan.FromMinutes(Math.Clamp(_settingsStore.Current.PollIntervalMinutes, 1, 120));
                // Back off a bit on failure so a persistent problem doesn't spin at full frequency.
                if (failed) interval = TimeSpan.FromMinutes(Math.Min(30, interval.TotalMinutes * 2));
                ScheduleNext(interval);
            }
        }
    }

    public void Dispose() => Stop();
}
