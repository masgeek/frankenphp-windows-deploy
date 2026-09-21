using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class ConfigPage : UserControl
{
    public ConfigPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadStatus();
    }

    private void LoadStatus()
    {
        var activeVersion = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
        {
            StatusBar.SetStatus("No active PHP version.");
            return;
        }

        var phpIni = Path.Combine(PhpService.BasePath, activeVersion, "php.ini");
        if (File.Exists(phpIni))
        {
            var content = File.ReadAllText(phpIni);
            var opcacheOn = content.Contains("opcache.enable") && !Regex.IsMatch(content, @"(?m)^\s*;?\s*opcache\.enable\s*=\s*Off");
            var displayOn = content.Contains("display_errors") && Regex.IsMatch(content, @"(?m)^\s*display_errors\s*=\s*On");
            StatusBar.SetStatus($"PHP {activeVersion} — php.ini exists. " +
                              $"OPcache: {(opcacheOn ? "ON" : "OFF")}, " +
                              $"display_errors: {(displayOn ? "ON" : "OFF")}");
        }
        else
        {
            StatusBar.SetStatus($"PHP {activeVersion} — no php.ini found.");
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var activeVersion = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
        {
            StatusBar.SetStatus("No active PHP version.");
            return;
        }

        var phpIni = Path.Combine(PhpService.BasePath, activeVersion, "php.ini");
        var devIni = Path.Combine(AppContext.BaseDirectory, "php.ini-development");
        var prodIni = Path.Combine(AppContext.BaseDirectory, "php.ini");

        try
        {
            if (DevRadio.IsChecked == true && File.Exists(devIni))
            {
                PhpService.ConfigurePhpIni(activeVersion, devIni);
                StatusBar.SetStatus("Applied php.ini-development.");
            }
            else if (ProdRadio.IsChecked == true && File.Exists(prodIni))
            {
                PhpService.ConfigurePhpIni(activeVersion, prodIni);
                StatusBar.SetStatus("Applied php.ini-production.");
            }
            else
            {
                StatusBar.SetStatus("No changes applied.");
            }
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }

        LoadStatus();
    }
}
