using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class RedisPage : UserControl
{
    public RedisPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadStatus();
    }

    private void LoadStatus()
    {
        var active = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(active))
        {
            StatusBar.SetStatus("No active PHP version. Install and set one first.");
            return;
        }

        var modules = PhpService.GetLoadedModules(active);
        if (modules.Contains("redis", StringComparer.OrdinalIgnoreCase))
            StatusBar.SetStatus($"PHP {active} — Redis extension is already installed and loaded.");
        else
            StatusBar.SetStatus($"PHP {active} — Redis not detected. Click Install to add it.");
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var activeVersion = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
        {
            StatusBar.SetStatus("No active PHP version. Install and set one first.");
            return;
        }

        var version = VersionBox.Text.Trim();
        if (string.IsNullOrEmpty(version))
        {
            StatusBar.SetStatus("Enter an extension version.");
            return;
        }

        InstallBtn.IsEnabled = false;
        StatusBar.SetStatus($"Installing Redis {version} for PHP {activeVersion}...", true);
        try
        {
            await PhpService.InstallRedisAsync(activeVersion, version, new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus($"Redis {version} installed and loaded on PHP {activeVersion}.");
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
