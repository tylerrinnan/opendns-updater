namespace OpenDnsUpdater;

internal sealed class SettingsForm : Form
{
    private readonly AppSettingsStore _store;

    private readonly TextBox _emailBox = new() { Width = 260 };
    private readonly TextBox _passwordBox = new() { Width = 260, UseSystemPasswordChar = true };
    private readonly CheckBox _showPasswordBox = new() { Text = "Show", AutoSize = true };
    private readonly TextBox _networkLabelBox = new() { Width = 260 };
    private readonly NumericUpDown _intervalBox = new() { Minimum = 1, Maximum = 120, Width = 80 };
    private readonly CheckBox _startWithWindowsBox = new() { Text = "Start automatically when I sign in to Windows", AutoSize = true };
    private readonly CheckBox _notifyOnSuccessBox = new() { Text = "Show a notification each time the IP is updated", AutoSize = true };
    private readonly Label _statusLabel = new() { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.DimGray };
    // AutoSize + MinimumSize, not a fixed Width/Height: a hardcoded pixel Height (the old
    // WinForms Button default is 23px, dating to 96 DPI) doesn't track the actual font/DPI
    // scale, so at a larger system font the label text needs more height than the button box
    // has — the text renders vertically clipped ("split in half"). AutoSize sizes the button
    // to what its content actually needs; MinimumSize just keeps a tidy minimum width.
    private readonly Button _testButton = new() { Text = "Test now", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(100, 0) };
    private readonly Button _okButton = new() { Text = "Save", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(90, 0), DialogResult = DialogResult.OK };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(90, 0), DialogResult = DialogResult.Cancel };

    public SettingsForm(AppSettingsStore store)
    {
        _store = store;

        Text = "OpenDNS Updater — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = SystemFonts.MessageBoxFont;

        BuildLayout();
        LoadFromSettings();

        _showPasswordBox.CheckedChanged += (_, _) => _passwordBox.UseSystemPasswordChar = !_showPasswordBox.Checked;
        _testButton.Click += async (_, _) => await RunTestAsync();
        _okButton.Click += (_, _) => SaveToSettings();
    }

    private void BuildLayout()
    {
        var fields = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void AddRow(string label, Control control, Control? extra = null)
        {
            var row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, row);
            fields.Controls.Add(control, 1, row);
            if (extra != null)
            {
                extra.Margin = new Padding(6, 3, 0, 0);
                fields.Controls.Add(extra, 2, row);
            }
        }

        AddRow("OpenDNS account email:", _emailBox);
        AddRow("Password:", _passwordBox, _showPasswordBox);
        AddRow("Network label:", _networkLabelBox);
        AddRow("Check interval (minutes):", _intervalBox);

        var helpLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 10, 0, 10),
            Text = "Find your network label at dashboard.opendns.com under your network's settings. " +
                   "If your account uses two-factor authentication, request an update-only password from " +
                   "OpenDNS support and use it above instead of your normal password.",
        };

        var checkboxPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Top };
        checkboxPanel.Controls.Add(_startWithWindowsBox);
        checkboxPanel.Controls.Add(_notifyOnSuccessBox);

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 14, 0, 0),
        };
        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_okButton);
        buttonPanel.Controls.Add(_testButton);

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        root.Controls.Add(fields);
        root.Controls.Add(helpLabel);
        root.Controls.Add(checkboxPanel);
        root.Controls.Add(_statusLabel);
        root.Controls.Add(buttonPanel);

        Controls.Add(root);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private void LoadFromSettings()
    {
        var s = _store.Current;
        _emailBox.Text = s.Email;
        _passwordBox.Text = s.GetPassword() ?? "";
        _networkLabelBox.Text = s.NetworkLabel;
        _intervalBox.Value = Math.Clamp(s.PollIntervalMinutes, (int)_intervalBox.Minimum, (int)_intervalBox.Maximum);
        _startWithWindowsBox.Checked = s.StartWithWindows;
        _notifyOnSuccessBox.Checked = s.NotifyOnSuccess;
    }

    private void SaveToSettings()
    {
        var s = _store.Current;
        s.Email = _emailBox.Text.Trim();
        s.NetworkLabel = _networkLabelBox.Text.Trim();
        s.SetPassword(_passwordBox.Text);
        s.PollIntervalMinutes = (int)_intervalBox.Value;
        s.StartWithWindows = _startWithWindowsBox.Checked;
        s.NotifyOnSuccess = _notifyOnSuccessBox.Checked;
        _store.Save();
    }

    private async Task RunTestAsync()
    {
        _testButton.Enabled = false;
        _statusLabel.ForeColor = Color.DimGray;
        _statusLabel.Text = "Checking your public IP and contacting OpenDNS...";
        try
        {
            var ip = await PublicIpResolver.GetPublicIpAsync(CancellationToken.None);
            if (ip is null)
            {
                _statusLabel.ForeColor = Color.Firebrick;
                _statusLabel.Text = "Couldn't determine your public IP address.";
                return;
            }

            var password = _passwordBox.Text;
            if (string.IsNullOrWhiteSpace(_emailBox.Text) || string.IsNullOrWhiteSpace(_networkLabelBox.Text) || string.IsNullOrWhiteSpace(password))
            {
                _statusLabel.ForeColor = Color.Firebrick;
                _statusLabel.Text = $"Your public IP is {ip}. Fill in email, password, and network label to test the update.";
                return;
            }

            var result = await OpenDnsClient.UpdateAsync(_emailBox.Text.Trim(), password, _networkLabelBox.Text.Trim(), ip, CancellationToken.None);
            _statusLabel.ForeColor = result.IsSuccess ? Color.SeaGreen : Color.Firebrick;
            _statusLabel.Text = $"{result.Status}: \"{result.RawResponse}\"";
            if (result.Status == OpenDnsUpdateStatus.BadAuth)
            {
                _statusLabel.Text += Environment.NewLine + OpenDnsClient.DescribeLikelyBadAuthCause(password);
            }
        }
        finally
        {
            _testButton.Enabled = true;
        }
    }
}
