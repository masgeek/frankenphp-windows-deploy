using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class VersionsPage : UserControl
{
    private List<PhpVersionInfo> _installedVersions = new();
    private List<string> _availableVersions = new();

    public VersionsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshAll();
    }

    private void RefreshAll()
    {
        LoadInstalled();
        LoadAvailable();
    }

    private void LoadInstalled()
    {
        _installedVersions = PhpService.GetInstalledVersions();
        InstalledList.ItemsSource = null;
        InstalledList.ItemsSource = _installedVersions;
        var active = PhpService.GetActiveVersion();
        var activeLabel = active == "frankenphp" ? "FrankenPHP" : string.IsNullOrEmpty(active) ? "(none)" : active;
        StatusBar.SetStatus(_installedVersions.Count == 0
            ? "No PHP versions installed."
            : $"{_installedVersions.Count} version(s) installed. Active: {activeLabel}");
    }

    private void LoadAvailable()
    {
        _availableVersions = PhpService.GetAvailableVersions();
        AvailableList.ItemsSource = null;
        AvailableList.ItemsSource = _availableVersions;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        RefreshBtn.IsEnabled = enabled;
        SetActiveBtn.IsEnabled = enabled;
        RemoveBtn.IsEnabled = enabled;
        RefreshCatalogBtn.IsEnabled = enabled;
        InstallBtn.IsEnabled = enabled;
        SystemPathCheck.IsEnabled = enabled;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e)
    {
        SetButtonsEnabled(false);
        StatusBar.SetStatus("Refreshing catalog...", true);
        try
        {
            var count = await PhpService.RefreshCatalogAsync();
            LoadAvailable();
            StatusBar.SetStatus($"Catalog refreshed: {count} versions available.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void InstallSelected_Click(object sender, RoutedEventArgs e)
    {
        var version = AvailableList.SelectedItem as string;
        if (string.IsNullOrEmpty(version))
        {
            StatusBar.SetStatus("Select a version from the Available list first.");
            return;
        }

        SetButtonsEnabled(false);
        StatusBar.SetStatus($"Installing PHP {version}...", true);
        try
        {
            var result = await PhpService.InstallVersionAsync(version, new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            ConfigureIni(version);
            StatusBar.SetStatus($"{result}. Setting as active...", true);
            PhpService.SetActiveVersion(version, SystemPathCheck.IsChecked == true);
            RefreshAll();
            StatusBar.SetStatus($"PHP {version} installed and set as active.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private void SetActive_Click(object sender, RoutedEventArgs e)
    {
        var selected = InstalledList.SelectedItem as PhpVersionInfo;
        if (selected == null)
        {
            StatusBar.SetStatus("Select an installed version first.");
            return;
        }

        if (selected.Active)
        {
            StatusBar.SetStatus($"{selected.Version} is already active.");
            return;
        }

        try
        {
            var setActiveTo = selected.IsFrankenPhp ? "frankenphp" : selected.Version;
            PhpService.SetActiveVersion(setActiveTo);

            var msg = $"Switched to {selected.Version}.";

            if (selected.IsFrankenPhp)
            {
                if (PhpService.IsRunningAsAdmin())
                {
                    PhpService.UpdateFrankenPhpSystemPath();
                    msg += " Added to system PATH.";
                }
                else if (SystemPathCheck.IsChecked == true)
                {
                    StatusBar.SetStatus("Requesting admin elevation for system PATH...", true);
                    var success = PhpService.RunElevated("--set-frankenphp-path");
                    msg += success ? " Added to system PATH." : " System PATH update cancelled (admin required).";
                }
            }
            else if (SystemPathCheck.IsChecked == true)
            {
                if (PhpService.IsRunningAsAdmin())
                {
                    PhpService.UpdateSystemPathElevated(selected.InstallDir);
                    msg += " Added to system PATH.";
                }
                else
                {
                    StatusBar.SetStatus("Requesting admin elevation for system PATH...", true);
                    var success = PhpService.RunElevated($"--set-system-path \"{selected.InstallDir}\"");
                    msg += success ? " Added to system PATH." : " System PATH update cancelled (admin required).";
                }
            }

            StatusBar.SetStatus(msg);
            LoadInstalled();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = InstalledList.SelectedItem as PhpVersionInfo;
        if (selected == null)
        {
            StatusBar.SetStatus("Select an installed version first.");
            return;
        }

        var result = MessageBox.Show(
            $"Remove PHP {selected.Version}?\nPath: {selected.InstallDir}",
            "Confirm Remove",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        SetButtonsEnabled(false);
        StatusBar.SetStatus($"Removing PHP {selected.Version}...", true);
        try
        {
            await PhpService.RemoveVersionAsync(selected.Version);
            StatusBar.SetStatus($"PHP {selected.Version} removed.");
            RefreshAll();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private void ConfigureIni(string version)
    {
        var devIni = Path.Combine(AppContext.BaseDirectory, "php.ini-development");
        var prodIni = Path.Combine(AppContext.BaseDirectory, "php.ini");

        if (File.Exists(devIni))
            PhpService.ConfigurePhpIni(version, devIni);
        else if (File.Exists(prodIni))
            PhpService.ConfigurePhpIni(version, prodIni);
    }
}
