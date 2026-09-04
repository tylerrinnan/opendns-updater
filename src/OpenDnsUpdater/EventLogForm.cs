namespace OpenDnsUpdater;

/// <summary>Shows the "last checks" history and the most recent successful update at a glance.
/// Unlike <see cref="SettingsForm"/> this window is resizable and list-driven, so it's laid out
/// with plain Dock.Fill/Top/Bottom on a fixed-size Form rather than Form.AutoSize — no
/// AutoSize-vs-Dock.Fill interaction to worry about because the Form itself doesn't AutoSize.</summary>
internal sealed class EventLogForm : Form
{
    private readonly Label _summaryLabel = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        Dock = DockStyle.Fill,
    };
    private readonly Button _openLogButton = new() { Text = "Open raw log file", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(90, 0) };
    private readonly Button _closeButton = new() { Text = "Close", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(90, 0), DialogResult = DialogResult.OK };

    public EventLogForm(AppSettingsStore settingsStore, EventHistoryStore historyStore, Action openRawLog)
    {
        Text = "OpenDNS Updater — Event Log";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 480);
        MinimumSize = new Size(420, 300);
        Padding = new Padding(16);
        Font = SystemFonts.MessageBoxFont;
        AcceptButton = _closeButton;
        CancelButton = _closeButton;

        _list.Columns.Add("Time", 170);
        _list.Columns.Add("Status", 85);
        var detailsColumn = _list.Columns.Add("Details", 360);
        // Keep Details filling the remaining width as the (resizable) window is resized,
        // rather than leaving it a fixed 360px with a horizontal scrollbar or dead space.
        _list.Resize += (_, _) =>
        {
            var fillWidth = _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[1].Width;
            detailsColumn.Width = Math.Max(100, fillWidth);
        };

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Margin = new Padding(0, 14, 0, 0),
        };
        buttonPanel.Controls.Add(_closeButton);
        buttonPanel.Controls.Add(_openLogButton);

        Controls.Add(_list);
        Controls.Add(buttonPanel);
        Controls.Add(_summaryLabel);
        _summaryLabel.Dock = DockStyle.Top;

        _openLogButton.Click += (_, _) => openRawLog();

        Populate(settingsStore, historyStore);
    }

    private void Populate(AppSettingsStore settingsStore, EventHistoryStore historyStore)
    {
        var s = settingsStore.Current;
        _summaryLabel.Text = s.LastUpdateUtc is { } lastUpdate
            ? $"Last update attempt: {lastUpdate.ToLocalTime():g} — IP {s.LastKnownIp ?? "unknown"} ({s.LastResult})"
            : "No update has been attempted yet.";

        foreach (var record in historyStore.Snapshot())
        {
            var item = new ListViewItem(record.TimestampUtc.ToLocalTime().ToString("g"));
            item.SubItems.Add(record.Kind.ToString());
            item.SubItems.Add(record.Message);
            item.ForeColor = record.Kind switch
            {
                EventKind.Error => Color.Firebrick,
                EventKind.Warning => Color.DarkOrange,
                EventKind.Success => Color.SeaGreen,
                _ => SystemColors.ControlText,
            };
            _list.Items.Add(item);
        }

        if (_list.Items.Count == 0)
        {
            _list.Items.Add(new ListViewItem(new[] { "", "", "No checks recorded yet." }));
        }
    }
}
