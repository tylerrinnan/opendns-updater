namespace OpenDnsUpdater;

internal static class Program
{
    // Named so a second launch (e.g. from the Startup shortcut while one copy is
    // already running) can detect the existing instance instead of piling up icons.
    private const string SingleInstanceMutexName = "Local\\OpenDnsUpdater.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show(
                "OpenDNS Updater is already running — look for its icon in the system tray.",
                "OpenDNS Updater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
