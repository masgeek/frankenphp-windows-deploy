using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class ServyPage : UserControl
{
    public ServyPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadStatus();
    }

    private void LoadStatus()
    {
        if (PhpService.IsServyInstalled())
        {
            var version = GetServyVersion();
            ServyStatus.Text = $"servy-cli is installed.\nPath: {GetServyPath()}\nVersion: {version}";
            InstallBtn.Visibility = Visibility.Collapsed;
            UninstallBtn.Visibility = Visibility.Visible;
        }
        else
        {
            ServyStatus.Text = "servy-cli is not installed.";
            InstallBtn.Visibility = Visibility.Visible;
            UninstallBtn.Visibility = Visibility.Collapsed;
        }
    }

    private static string GetServyPath()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "servy-cli",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return string.IsNullOrEmpty(output) ? "Not found in PATH" : output.Split('\n')[0].Trim();
        }
        catch { return "Unknown"; }
    }

    private static string GetServyVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "servy-cli",
                Arguments = "version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadLine() ?? "";
            process.WaitForExit();
            return string.IsNullOrEmpty(output) ? "Unknown" : output.Trim();
        }
        catch { return "Unknown"; }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        SetButtonsEnabled(false);
        StatusBar.SetStatus("Installing servy-cli...", true);
        try
        {
            await PhpService.InstallServyAsync(new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus("servy-cli installed successfully. You may need to restart the application.");
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

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Remove servy-cli?\nThis will delete the servy executable from disk.",
            "Confirm Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var servyDir = System.IO.Path.Combine(PhpService.FrankenPhpPath, "servy");
            if (System.IO.Directory.Exists(servyDir))
            {
                System.IO.Directory.Delete(servyDir, recursive: true);
                StatusBar.SetStatus("servy-cli uninstalled.");
            }
            else
            {
                StatusBar.SetStatus("servy-cli directory not found.");
            }
            LoadStatus();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadStatus();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        InstallBtn.IsEnabled = enabled;
        UninstallBtn.IsEnabled = enabled;
        RefreshBtn.IsEnabled = enabled;
    }
}
