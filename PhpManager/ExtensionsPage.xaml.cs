using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace PhpManager;

public partial class ExtensionsPage : UserControl
{
    private readonly ObservableCollection<ExtensionInfo> _extensions = new();

    public ExtensionsPage()
    {
        InitializeComponent();
        ExtensionGrid.ItemsSource = _extensions;
        Loaded += (_, _) => LoadExtensions();
    }

    private void LoadExtensions()
    {
        _extensions.Clear();
        foreach (var ext in PhpService.GetExtensions())
            _extensions.Add(ext);

        var active = PhpService.GetActiveVersion();
        StatusBar.SetStatus($"Loaded {_extensions.Count} extensions for PHP {(string.IsNullOrEmpty(active) ? "(none)" : active)}");
    }

    private void LoadExtensions_Click(object sender, System.Windows.RoutedEventArgs e) => LoadExtensions();

    private void SaveExtensions_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var activeVersion = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
        {
            StatusBar.SetStatus("No active PHP version. Install and set one first.");
            return;
        }

        try
        {
            PhpService.SaveExtensions(activeVersion, _extensions.ToList());
            StatusBar.SetStatus($"Saved {_extensions.Count} extension settings.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private async void Verify_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var activeVersion = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
        {
            StatusBar.SetStatus("No active PHP version.");
            return;
        }

        VerifyBtn.IsEnabled = false;
        StatusBar.SetStatus("Verifying extensions...", true);
        try
        {
            var (loaded, failed, noDll) = await Task.Run(() => PhpService.VerifyExtensions(activeVersion));

            var phpVersion = await Task.Run(() => PhpService.GetPhpVersionString(activeVersion));
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(phpVersion))
                lines.Add(phpVersion);

            if (loaded.Count > 0)
                lines.Add($"Loaded ({loaded.Count}): {string.Join(", ", loaded)}");
            if (failed.Count > 0)
                lines.Add($"Failed ({failed.Count}): {string.Join(", ", failed)}");
            if (noDll.Count > 0)
                lines.Add($"No DLL ({noDll.Count}): {string.Join(", ", noDll)}");

            if (loaded.Count + failed.Count + noDll.Count == 0)
                lines.Add("No extensions enabled in php.ini.");

            StatusBar.SetStatus(string.Join(" | ", lines));
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            VerifyBtn.IsEnabled = true;
        }
    }
}
