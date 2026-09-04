namespace OpenDnsUpdater;

/// <summary>
/// A window that is never shown. It exists only to give the tray app a real,
/// UI-thread-affine handle so background work (network events, timers) can safely
/// marshal back onto the UI thread via Invoke/BeginInvoke, and so modal dialogs
/// (Settings) have a proper owner window.
/// </summary>
internal sealed class HiddenForm : Form
{
    public HiddenForm()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-2000, -2000);
        Size = new Size(1, 1);
        Opacity = 0;
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);
}
