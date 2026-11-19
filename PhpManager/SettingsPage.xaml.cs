using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PhpManager;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = PhpService.LoadSettings();
        BasePathBox.Text = settings.BasePath;
        FrankenPhpPathBox.Text = settings.FrankenPhpPath;
        StatusBar.SetStatus("Settings loaded.");
    }

    private void BrowseBase_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder(BasePathBox.Text);
        if (path != null)
            BasePathBox.Text = path;
    }

    private void BrowseFranken_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder(FrankenPhpPathBox.Text);
        if (path != null)
            FrankenPhpPathBox.Text = path;
    }

    private static string? PickFolder(string? currentPath)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder",
            InitialDirectory = Directory.Exists(currentPath) ? currentPath : "",
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var basePath = BasePathBox.Text.Trim();
        var frankenPath = FrankenPhpPathBox.Text.Trim();

        if (string.IsNullOrEmpty(basePath))
        {
            StatusBar.SetStatus("PHP installation path is required.");
            return;
        }

        try
        {
            var settings = new PhpService.AppSettings
            {
                BasePath = basePath,
                FrankenPhpPath = string.IsNullOrEmpty(frankenPath) ? Path.Combine(basePath, "frankenphp") : frankenPath
            };
            PhpService.SaveSettings(settings);
            StatusBar.SetStatus("Settings saved. Restart may be needed for some changes to take effect.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private void ExportBundle_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export all settings",
            Filter = "PHP Manager settings (*.json)|*.json",
            FileName = $"php-manager-settings-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var bundle = FeatureService.CreateBundle();
            File.WriteAllText(dialog.FileName, bundle.ToJson(), new System.Text.UTF8Encoding(false));
            StatusBar.SetSuccess($"Exported to {dialog.FileName} ({bundle.Directives.Count} php.ini settings, " +
                                 $"{bundle.EnabledExtensions.Count} enabled extensions).");
        }
        catch (Exception ex)
        {
            StatusBar.SetError($"Export failed: {ex.Message}");
        }
    }

    private void ImportBundle_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import settings",
            Filter = "PHP Manager settings (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var bundle = SettingsBundle.FromJson(File.ReadAllText(dialog.FileName));
            var answer = MessageBox.Show(
                $"Import settings exported on {bundle.ExportedAt}?{Environment.NewLine}{Environment.NewLine}" +
                $"This replaces your paths and URLs and applies {bundle.Directives.Count} php.ini setting(s) and " +
                $"{bundle.EnabledExtensions.Count} extension(s) to the active PHP. php.ini is backed up first.",
                "Import settings", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;

            StatusBar.SetSuccess(FeatureService.ApplyBundle(bundle) + " Restart the app to use new paths.");
        }
        catch (Exception ex)
        {
            StatusBar.SetError($"Import failed: {ex.Message}");
        }
    }
}
