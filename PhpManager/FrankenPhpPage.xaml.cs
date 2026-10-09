using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PhpManager;

public partial class FrankenPhpPage : UserControl
{
    private FrankenPhpServiceRecord? _editingRecord;

    public FrankenPhpPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshAll();
    }

    private void RefreshAll()
    {
        LoadStatus();
        CheckServy();
        LoadServices();
        ClearForm();
    }

    private void CheckServy()
    {
        if (!PhpService.IsServyInstalled())
        {
            var result = MessageBox.Show(
                "servy-cli is required for FrankenPHP service management.\n\nInstall it now?",
                "servy-cli Not Found",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SetButtonsEnabled(false);
                StatusBar.SetStatus("Installing servy-cli...", true);
                try
                {
                    PhpService.InstallServyAsync(new Progress<string>(msg =>
                        Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true)))).Wait();
                    StatusBar.SetStatus("servy-cli installed. You may need to restart the application.");
                }
                catch (Exception ex)
                {
                    StatusBar.SetStatus($"Failed to install servy-cli: {ex.Message}");
                }
                finally
                {
                    SetButtonsEnabled(true);
                }
            }
            else
            {
                StatusBar.SetStatus("servy-cli is required. Service management will not work without it.");
            }
        }
    }

    private void LoadStatus()
    {
        var installed = PhpService.IsFrankenPhpInstalled();
        var version = PhpService.GetFrankenPhpVersion();
        FrankenPhpStatus.Text = installed
            ? $"Installed at {PhpService.FrankenPhpPath}\nVersion: {version}"
            : "Not installed. Click 'Install / Update Runtime' to download.";
    }

    private void LoadServices()
    {
        FrankenPhpDatabase.Initialize();
        var services = FrankenPhpDatabase.GetAll();
        foreach (var svc in services)
            svc.IsRunning = PhpService.IsServiceRunning(svc.ServiceName);

        ServicesGrid.ItemsSource = null;
        ServicesGrid.ItemsSource = services;
    }

    private void ClearForm()
    {
        _editingRecord = null;
        FormTitle.Text = "New Service";
        ServiceNameBox.Text = "";
        DisplayNameBox.Text = "";
        AppPathBox.Text = "";
        PortBox.Text = "8001";
        AdminPortBox.Text = "2020";
        WorkersBox.Text = "2";
        MaxRequestsBox.Text = "250";
        HealthPathBox.Text = "/up";
        FirewallRuleNameBox.Text = "";
        DevModeCheck.IsChecked = false;
        DeleteBtn.Visibility = Visibility.Collapsed;
    }

    private void LoadForm(FrankenPhpServiceRecord record)
    {
        _editingRecord = record;
        FormTitle.Text = $"Editing: {record.ServiceName}";
        ServiceNameBox.Text = record.ServiceName;
        DisplayNameBox.Text = record.DisplayName;
        AppPathBox.Text = record.AppPath;
        PortBox.Text = record.Port.ToString();
        AdminPortBox.Text = record.AdminPort.ToString();
        WorkersBox.Text = record.Workers.ToString();
        MaxRequestsBox.Text = record.MaxRequests.ToString();
        HealthPathBox.Text = record.HealthPath;
        FirewallRuleNameBox.Text = record.FirewallRuleName;
        DevModeCheck.IsChecked = record.IsDevelopment;
        DeleteBtn.Visibility = Visibility.Visible;
    }

    private FrankenPhpServiceRecord FormToRecord()
    {
        return new FrankenPhpServiceRecord
        {
            Id = _editingRecord?.Id ?? 0,
            ServiceName = ServiceNameBox.Text.Trim(),
            DisplayName = DisplayNameBox.Text.Trim(),
            Description = "",
            AppPath = AppPathBox.Text.Trim(),
            Port = int.TryParse(PortBox.Text, out var p) ? p : 8001,
            AdminPort = int.TryParse(AdminPortBox.Text, out var ap) ? ap : 2020,
            Workers = int.TryParse(WorkersBox.Text, out var w) ? w : 2,
            MaxRequests = int.TryParse(MaxRequestsBox.Text, out var mr) ? mr : 250,
            HealthPath = HealthPathBox.Text.Trim(),
            FirewallRuleName = FirewallRuleNameBox.Text.Trim(),
            IsDevelopment = DevModeCheck.IsChecked == true
        };
    }

    private void ServicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServicesGrid.SelectedItem is FrankenPhpServiceRecord record)
            LoadForm(record);
    }

    private void BrowseAppPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Laravel Application Directory",
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
            AppPathBox.Text = dialog.FolderName;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var record = FormToRecord();

        if (string.IsNullOrEmpty(record.ServiceName))
        {
            StatusBar.SetStatus("Service name is required.");
            return;
        }
        if (string.IsNullOrEmpty(record.AppPath))
        {
            StatusBar.SetStatus("Application path is required.");
            return;
        }
        if (!File.Exists(Path.Combine(record.AppPath, "artisan")))
        {
            StatusBar.SetStatus("Laravel artisan not found at the specified path.");
            return;
        }

        SetButtonsEnabled(false);
        StatusBar.SetStatus($"Saving service '{record.ServiceName}'...", true);
        try
        {
            await PhpService.InstallFrankenPhpServiceAsync(record, new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus($"Service '{record.ServiceName}' saved successfully.");
            LoadServices();
            ClearForm();
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ClearForm();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingRecord == null) return;

        var result = MessageBox.Show(
            $"Delete service '{_editingRecord.ServiceName}'?\nThis will uninstall the Windows service.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        SetButtonsEnabled(false);
        StatusBar.SetStatus($"Deleting service '{_editingRecord.ServiceName}'...", true);
        try
        {
            await PhpService.RemoveFrankenPhpServiceAsync(_editingRecord, new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus($"Service '{_editingRecord.ServiceName}' deleted.");
            LoadServices();
            ClearForm();
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

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var record = ServicesGrid.SelectedItem as FrankenPhpServiceRecord;
        if (record == null)
        {
            StatusBar.SetStatus("Select a service to start.");
            return;
        }

        SetButtonsEnabled(false);
        StatusBar.SetStatus($"Starting '{record.ServiceName}'...", true);
        try
        {
            await PhpService.StartFrankenPhpServiceAsync(record, new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus($"Service '{record.ServiceName}' started.");
            LoadServices();
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

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        var record = ServicesGrid.SelectedItem as FrankenPhpServiceRecord;
        if (record == null)
        {
            StatusBar.SetStatus("Select a service to stop.");
            return;
        }

        SetButtonsEnabled(false);
        StatusBar.SetStatus($"Stopping '{record.ServiceName}'...", true);
        try
        {
            await PhpService.StopFrankenPhpServiceAsync(record, new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus($"Service '{record.ServiceName}' stopped.");
            LoadServices();
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

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAll();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        SetButtonsEnabled(false);
        StatusBar.SetStatus("Installing FrankenPHP...", true);
        try
        {
            await PhpService.InstallFrankenPhpAsync(progress: new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus("FrankenPHP installed successfully.");
            LoadStatus();
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

    private void SetPath_Click(object sender, RoutedEventArgs e)
    {
        if (PhpService.IsRunningAsAdmin())
        {
            PhpService.UpdateFrankenPhpSystemPath();
            StatusBar.SetStatus("FrankenPHP added to system PATH.");
        }
        else
        {
            var success = PhpService.RunElevated("--set-frankenphp-path");
            StatusBar.SetStatus(success ? "FrankenPHP added to system PATH." : "System PATH update cancelled (admin required).");
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        InstallBtn.IsEnabled = enabled;
        SetPathBtn.IsEnabled = enabled;
        SaveBtn.IsEnabled = enabled;
        DeleteBtn.IsEnabled = enabled;
        StartBtn.IsEnabled = enabled;
        StopBtn.IsEnabled = enabled;
        RefreshBtn.IsEnabled = enabled;
    }
}
