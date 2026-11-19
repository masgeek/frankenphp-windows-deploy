using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class ComposerPage : UserControl
{
    public ComposerPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "Checking...";
        var (installed, version) = await Task.Run(() => (ComposerService.IsInstalled(), ComposerService.GetVersion()));

        StatusText.Text = !installed
            ? $"Not installed. It will be placed in {ComposerService.Dir}."
            : version != null
                ? $"Composer {version} at {ComposerService.PharPath}"
                : $"composer.phar is at {ComposerService.PharPath}, but it did not run. Check that a PHP version is active (see Health Check).";

        var home = ComposerService.CurrentComposerHome();
        HomeText.Text = string.IsNullOrEmpty(home)
            ? $"COMPOSER_HOME is not set (Composer uses its default under %APPDATA%). Suggested: {ComposerService.HomePath}"
            : $"COMPOSER_HOME = {home}";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallBtn.IsEnabled = false;
        StatusBar.SetStatus("Installing Composer...", true);
        try
        {
            await ComposerService.InstallAsync(new Progress<string>(m => Dispatcher.Invoke(() => StatusBar.SetStatus(m, true))));
            await RefreshAsync();
            StatusBar.SetStatus("Composer installed. Open a new terminal to use composer.");
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

    private async void SetHome_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ComposerService.SetComposerHome();
            await RefreshAsync();
            StatusBar.SetStatus("COMPOSER_HOME set. Open a new terminal to pick it up.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }
}
