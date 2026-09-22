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
}
