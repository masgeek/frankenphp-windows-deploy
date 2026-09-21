using System.Windows;

namespace PhpManager;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--set-system-path")
        {
            var versionDir = e.Args[1];
            try
            {
                PhpService.UpdateSystemPathElevated(versionDir);
                MessageBox.Show($"Added to system PATH:\n{versionDir}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
