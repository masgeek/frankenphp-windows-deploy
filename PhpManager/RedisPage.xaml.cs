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
        DocsLink.Text = $"Docs: {Urls.Docs.Link("Redis")}";

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

    private async void RefreshVersions_Click(object sender, RoutedEventArgs e)
    {
        RefreshVersionsBtn.IsEnabled = false;
        StatusBar.SetStatus("Fetching available Redis versions...", true);
        try
        {
            var versions = await PhpService.GetAvailableExtensionVersionsAsync(
                Urls.Redis.VersionIndex,
                new Progress<string>(msg => Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));

            if (versions.Count == 0)
            {
                StatusBar.SetStatus("No versions found. Check the Redis version index URL.");
                return;
            }

            var selected = VersionBox.Text;
            VersionBox.ItemsSource = versions;
            VersionBox.Text = versions.Contains(selected) ? selected : versions[0];

            StatusBar.SetSuccess($"Found {versions.Count} Redis version(s).");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            RefreshVersionsBtn.IsEnabled = true;
        }
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