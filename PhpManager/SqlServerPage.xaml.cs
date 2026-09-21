using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class SqlServerPage : UserControl
{
    public SqlServerPage()
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
        var hasSql = modules.Contains("sqlsrv", StringComparer.OrdinalIgnoreCase);
        var hasPdo = modules.Contains("pdo_sqlsrv", StringComparer.OrdinalIgnoreCase);

        if (hasSql && hasPdo)
            StatusBar.SetStatus($"PHP {active} — SQL Server drivers already installed and loaded.");
        else if (hasSql || hasPdo)
            StatusBar.SetStatus($"PHP {active} — Partially installed (sqlsrv: {(hasSql ? "yes" : "no")}, pdo_sqlsrv: {(hasPdo ? "yes" : "no")}). Click Install to fix.");
        else
            StatusBar.SetStatus($"PHP {active} — SQL Server drivers not detected. Click Install to add them.");
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
            StatusBar.SetStatus("Enter a driver version.");
            return;
        }

        InstallBtn.IsEnabled = false;
        StatusBar.SetStatus($"Installing SQL Server drivers {version} for PHP {activeVersion}...", true);
        try
        {
            await PhpService.InstallSqlServerAsync(activeVersion, version, new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus($"SQL Server drivers {version} installed on PHP {activeVersion}.");
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
