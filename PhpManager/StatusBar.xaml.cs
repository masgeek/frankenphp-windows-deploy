using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class StatusBar : UserControl
{
    public StatusBar()
    {
        InitializeComponent();
    }

    public void SetStatus(string message, bool showProgress = false, double? percent = null)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            LogBtn.Visibility = Visibility.Visible;

            if (percent.HasValue)
            {
                Spinner.Visibility = Visibility.Collapsed;
                Progress.Visibility = Visibility.Visible;
                Progress.Value = percent.Value;
            }
            else if (showProgress)
            {
                Spinner.Visibility = Visibility.Visible;
                Progress.Visibility = Visibility.Collapsed;
            }
            else
            {
                Spinner.Visibility = Visibility.Collapsed;
                Progress.Visibility = Visibility.Collapsed;
            }
        });

        if (showProgress || percent.HasValue)
            LogWindow.Log(message);
    }

    public void SetSuccess(string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            Spinner.Visibility = Visibility.Collapsed;
            Progress.Visibility = Visibility.Collapsed;
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
            Progress.Visibility = Visibility.Collapsed;
            LogBtn.Visibility = Visibility.Visible;
        });
        LogWindow.LogError(message);
    }

    private void LogBtn_Click(object sender, RoutedEventArgs e)
    {
        LogWindow.Instance.ShowLog();
    }
}
