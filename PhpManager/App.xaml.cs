using System.Windows;

namespace PhpManager;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--set-system-path")
        {
            try
            {
                PhpService.UpdateSystemPathElevated(e.Args[1]);
                MessageBox.Show($"Added to system PATH:\n{e.Args[1]}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        if (e.Args.Length == 1 && e.Args[0] == "--set-frankenphp-path")
        {
            try
            {
                PhpService.UpdateFrankenPhpSystemPath();
                MessageBox.Show("FrankenPHP added to system PATH.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
