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
        Loaded += async (_, _) =>
        {
            await RefreshAllAsync();
            // Fresh machine: no cached catalog yet, so fetch it automatically.
            if (!PhpService.HasCatalogCache() && RefreshCatalogBtn.IsEnabled)
                await RefreshCatalogCoreAsync();
        };
    }

    private async Task RefreshAllAsync()
    {
        LoadAvailable();
        await LoadInstalledAsync();
        ShowUpdates();
    }

    private void ShowUpdates()
    {
        var installed = _installedVersions
            .Where(v => v.Installed && !v.IsFrankenPhp && !v.IsExternal)
            .Select(v => v.DetectedVersion ?? v.Version);

        var updates = UpdateChecker.FindUpdates(installed, _availableVersions);
        UpdatesList.ItemsSource = updates;
        UpdatesCard.Visibility = updates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (updates.Count > 0)
            UpdatesList.SelectedIndex = 0;
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (UpdatesList.SelectedItem is not PhpUpdate update)
        {
            StatusBar.SetStatus("Select an update first.");
            return;
        }

        var old = _installedVersions.FirstOrDefault(v => (v.DetectedVersion ?? v.Version) == update.Installed && !v.IsFrankenPhp);
        if (old == null)
        {
            StatusBar.SetStatus($"PHP {update.Installed} is no longer installed.");
            return;
        }

        SetButtonsEnabled(false);
        StatusBar.SetStatus($"Updating {update.Installed} to {update.Latest}...", true);
        try
        {
            var message = await FeatureService.UpgradeAsync(old.Version, update.Latest,
                new Progress<string>(m => Dispatcher.Invoke(() => StatusBar.SetStatus(m, true))));
            await RefreshAllAsync();
            StatusBar.SetStatus(message);
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

    private async Task LoadInstalledAsync()
    {
        // Reads php.exe versions (spawns processes); keep it off the UI thread.
        _installedVersions = await Task.Run(PhpService.GetInstalledVersions);
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
        UpdateBtn.IsEnabled = enabled;
        SystemPathCheck.IsEnabled = enabled;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e) => await RefreshCatalogCoreAsync();

    private async Task RefreshCatalogCoreAsync()
    {
        SetButtonsEnabled(false);
        StatusBar.SetStatus("Refreshing catalog...", true);
        try
        {
            var count = await PhpService.RefreshCatalogAsync();
            LoadAvailable();
            ShowUpdates();
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
            var iniResult = ConfigureIni(version);
            StatusBar.SetStatus($"{result}. {iniResult} Setting as active...", true);
            PhpService.SetActiveVersion(version, SystemPathCheck.IsChecked == true);
            await RefreshAllAsync();
            StatusBar.SetStatus($"PHP {version} installed and set as active. {iniResult}");
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

    private async void SetActive_Click(object sender, RoutedEventArgs e)
    {
        var selected = InstalledList.SelectedItem as PhpVersionInfo;
        if (selected == null)
        {
            StatusBar.SetStatus("Select an installed version first.");
            return;
        }

        if (selected.IsExternal)
        {
            StatusBar.SetStatus($"PHP {selected.Version} is already on PATH ({selected.InstallDir}). Install a managed version to switch.");
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
            await LoadInstalledAsync();
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

        if (selected.IsExternal)
        {
            StatusBar.SetStatus("PHP found on PATH is not managed by PHP Manager and can't be removed here.");
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
            var machinePathPending = await PhpService.RemoveVersionAsync(selected.Version);
            var msg = $"PHP {selected.Version} removed and PATH updated.";

            if (machinePathPending)
            {
                StatusBar.SetStatus("Requesting admin elevation to clean the system PATH...", true);
                var dir = selected.InstallDir;
                var ok = await Task.Run(() => PhpService.RunElevated($"--remove-system-path \"{dir}\""));
                msg = ok
                    ? $"PHP {selected.Version} removed and PATH updated (including system PATH)."
                    : $"PHP {selected.Version} removed. System PATH still lists it (admin required to remove).";
            }

            await RefreshAllAsync();
            StatusBar.SetStatus(msg);
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

    /// <summary>Creates php.ini per the selected profile; "None" leaves it alone. Failure is reported, not fatal.</summary>
    private string ConfigureIni(string version)
    {
        var profile = IniProfileCombo.SelectedIndex switch
        {
            0 => IniProfile.Development,
            1 => IniProfile.Production,
            _ => (IniProfile?)null
        };

        if (profile == null)
            return "php.ini not created.";

        try
        {
            return PhpService.ApplyIniProfile(version, profile.Value);
        }
        catch (Exception ex)
        {
            return $"php.ini not created: {ex.Message}";
        }
    }
}
