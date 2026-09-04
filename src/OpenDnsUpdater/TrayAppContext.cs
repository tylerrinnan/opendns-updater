using System.Diagnostics;

namespace OpenDnsUpdater;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly HiddenForm _hiddenForm = new();
    private readonly NotifyIcon _trayIcon;
    private readonly AppSettingsStore _settingsStore = new();
    private readonly EventHistoryStore _history = new();
    private readonly IpMonitorService _monitor;
    private DateTimeOffset _lastWarningBalloon = DateTimeOffset.MinValue;

    public TrayAppContext()
    {
        // Force real handle creation now. CreateControl() alone is a no-op here because the
        // form's Visible is permanently false — accessing Handle is what actually creates it,
        // regardless of visibility — and that handle is what makes Invoke/BeginInvoke work.
        _ = _hiddenForm.Handle;

        _trayIcon = new NotifyIcon
        {
            Icon = TrayIcons.Idle,
            Text = "OpenDNS Updater",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _trayIcon.DoubleClick += (_, _) => OpenSettings();

        _monitor = new IpMonitorService(_settingsStore);
        _monitor.StatusChanged += OnStatusChanged;
        _monitor.UpdateCompleted += OnUpdateCompleted;

        // Keep the registry entry in sync with the saved preference, in case the exe moved.
        if (_settingsStore.Current.StartWithWindows) AutoStartManager.Enable();

        if (!_settingsStore.Current.IsConfigured)
        {
            _trayIcon.ShowBalloonTip(
                8000, "OpenDNS Updater", "Not set up yet — right-click the tray icon and choose Settings.", ToolTipIcon.Info);
        }

        AppLog.Info("Application started.");
        _monitor.Start();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Check now", null, (_, _) => _monitor.CheckAndUpdateAsync().FireAndForget("manual check"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open OpenDNS dashboard", null, (_, _) => OpenUrl("https://dashboard.opendns.com/"));
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add("Event log...", null, (_, _) => OpenEventLog());
        menu.Items.Add("View raw log file", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        return menu;
    }

    private void OnStatusChanged(string status)
    {
        _hiddenForm.BeginInvoke(new MethodInvoker(() =>
        {
            _trayIcon.Text = Truncate($"OpenDNS Updater — {status}", 127);
            AppLog.Info(status);
            // These StatusChanged messages cover both routine pings ("no change") and things
            // that stopped a check from completing (couldn't resolve IP, unreadable password) —
            // the latter didn't reach OnUpdateCompleted's own Error recording, so flag them here.
            var kind = status.Contains("couldn't", StringComparison.OrdinalIgnoreCase) ? EventKind.Warning : EventKind.Info;
            _history.Record(kind, status);
        }));
    }

    private void OnUpdateCompleted(OpenDnsUpdateResult result, string ip)
    {
        _hiddenForm.BeginInvoke(new MethodInvoker(() =>
        {
            switch (result.Status)
            {
                case OpenDnsUpdateStatus.Updated:
                    _trayIcon.Icon = TrayIcons.Idle;
                    _trayIcon.Text = Truncate($"OpenDNS Updater — updated to {ip}", 127);
                    AppLog.Info($"Updated OpenDNS network IP to {ip}.");
                    _history.Record(EventKind.Success, $"Updated OpenDNS network IP to {ip}.");
                    if (_settingsStore.Current.NotifyOnSuccess)
                    {
                        _trayIcon.ShowBalloonTip(5000, "OpenDNS Updater", $"Your OpenDNS network IP is now {ip}.", ToolTipIcon.Info);
                    }
                    break;

                case OpenDnsUpdateStatus.NoChange:
                    _trayIcon.Icon = TrayIcons.Idle;
                    _trayIcon.Text = Truncate($"OpenDNS Updater — up to date ({ip})", 127);
                    AppLog.Info($"IP unchanged ({ip}); OpenDNS confirms no update needed.");
                    _history.Record(EventKind.Info, $"IP unchanged ({ip}); no update needed.");
                    break;

                default:
                    _trayIcon.Icon = TrayIcons.Warning;
                    _trayIcon.Text = Truncate($"OpenDNS Updater — {result.Status}", 127);
                    AppLog.Error($"Update failed: {result.Status} (raw response: \"{result.RawResponse}\").");
                    _history.Record(EventKind.Error, $"Update failed: {result.Status} (raw response: \"{result.RawResponse}\").");
                    MaybeShowWarningBalloon(result.Status);
                    break;
            }
        }));
    }

    private void MaybeShowWarningBalloon(OpenDnsUpdateStatus status)
    {
        // Throttle so a persistent problem doesn't spam a balloon every poll cycle.
        if (DateTimeOffset.UtcNow - _lastWarningBalloon < TimeSpan.FromHours(1)) return;
        _lastWarningBalloon = DateTimeOffset.UtcNow;

        var message = status switch
        {
            OpenDnsUpdateStatus.BadAuth =>
                "OpenDNS rejected your credentials. This is often not the password being wrong — either it " +
                "contains a character (^ & ~ ` %) this API has a long-standing bug with, or your account has " +
                "two-factor authentication and needs a separate update-only password from OpenDNS support. " +
                "Open Settings and use Test now for a specific diagnosis.",
            OpenDnsUpdateStatus.NotYours => "That network label doesn't belong to your OpenDNS account. Check it in Settings.",
            OpenDnsUpdateStatus.NoHost => "OpenDNS doesn't recognize that network label. Check it in Settings.",
            OpenDnsUpdateStatus.DonatorOnly => "This feature requires an OpenDNS paid plan on your account.",
            OpenDnsUpdateStatus.Abuse => "OpenDNS has flagged this account/network for abuse — check the dashboard.",
            _ => "Couldn't update OpenDNS. Open the log for details.",
        };
        _trayIcon.ShowBalloonTip(10000, "OpenDNS Updater — action needed", message, ToolTipIcon.Warning);
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settingsStore);
        if (form.ShowDialog(_hiddenForm) == DialogResult.OK)
        {
            if (_settingsStore.Current.StartWithWindows) AutoStartManager.Enable();
            else AutoStartManager.Disable();
            _monitor.CheckAndUpdateAsync().FireAndForget("post-settings check");
        }
    }

    private void OpenEventLog()
    {
        using var form = new EventLogForm(_settingsStore, _history, OpenLog);
        form.ShowDialog(_hiddenForm);
    }

    private void OpenLog()
    {
        AppPaths.EnsureExists();
        if (!File.Exists(AppPaths.LogFile)) File.WriteAllText(AppPaths.LogFile, "");
        OpenUrl(AppPaths.LogFile);
    }

    private static void OpenUrl(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to open '{target}': {ex.Message}");
        }
    }

    private void ExitApplication()
    {
        AppLog.Info("Application exiting.");
        _monitor.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _hiddenForm.Dispose();
        ExitThread();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
