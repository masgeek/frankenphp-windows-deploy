using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class StatusBar : UserControl
{
    public StatusBar()
    {
        InitializeComponent();
    }

    public void SetStatus(string message, bool showProgress = false)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            Spinner.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
            LogBtn.Visibility = Visibility.Visible;
        });

        if (showProgress)
            LogWindow.Log(message);
    }

    public void SetSuccess(string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            Spinner.Visibility = Visibility.Collapsed;
            LogBtn.Visibility = Visibility.Visible;
        });
        LogWindow.LogSuccess(message);
    }

    public void SetError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            Spinner.Visibility = Visibility.Collapsed;
            LogBtn.Visibility = Visibility.Visible;
        });
        LogWindow.LogError(message);
    }

    private void LogBtn_Click(object sender, RoutedEventArgs e)
    {
        LogWindow.Instance.ShowLog();
    }
}
