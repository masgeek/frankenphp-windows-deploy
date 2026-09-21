using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class CacertPage : UserControl
{
    public CacertPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadStatus();
    }

    private void LoadStatus()
    {
        var activeVersion = PhpService.GetActiveVersion();
        StatusBar.SetStatus(string.IsNullOrEmpty(activeVersion)
            ? "No active PHP version."
            : $"PHP {activeVersion} — Click Install to download and configure CA certificate.");
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var activeVersion = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
        {
            StatusBar.SetStatus("No active PHP version. Install and set one first.");
            return;
        }

        InstallBtn.IsEnabled = false;
        StatusBar.SetStatus("Installing CA certificate...", true);
        try
        {
            await PhpService.InstallCacertAsync(activeVersion, new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus("CA certificate installed and php.ini configured.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            InstallBtn.IsEnabled = true;
        }
    }
}
