using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PhpManager;

public partial class LogWindow : Window
{
    private static LogWindow? _instance;
    private static readonly object _lock = new();

    public static LogWindow Instance
    {
        get
        {
            lock (_lock)
            {
                if (_instance == null || !_instance.IsLoaded)
                    _instance = new LogWindow();
                return _instance;
            }
        }
    }

    private LogWindow()
    {
        InitializeComponent();
        PositionNearMainWindow();
    }

    private void PositionNearMainWindow()
    {
        var main = Application.Current.MainWindow;
        if (main != null)
        {
            Left = main.Left + main.Width + 10;
            Top = main.Top;
            Owner = main;
        }
    }

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        var window = Instance;
        if (!window.IsLoaded)
            window.Show();

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var prefix = level switch
        {
            LogLevel.Error => "[ERROR]",
            LogLevel.Warn => "[WARN]",
            LogLevel.Success => "[OK]",
            _ => "[INFO]"
        };

        var color = level switch
        {
            LogLevel.Error => new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            LogLevel.Warn => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
            LogLevel.Success => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
            _ => new SolidColorBrush(Color.FromRgb(209, 213, 219))
        };

        var line = $"[{timestamp}] {prefix} {message}\n";

        window.Dispatcher.Invoke(() =>
        {
            var para = new Paragraph(new Run(line)) { Margin = new Thickness(0, 2, 0, 2) };
            para.Inlines.FirstInline.Foreground = color;
            window.LogBox.Document.Blocks.Add(para);
            window.LogBox.ScrollToEnd();
        });
    }

    public static void LogDownload(string url)
    {
        Log($"Downloading: {url}");
    }

    public static void LogExtract(string path)
    {
        Log($"Extracting to: {path}");
    }

    public static void LogService(string service, string action)
    {
        Log($"Service '{service}': {action}");
    }

    public static void LogSuccess(string message)
    {
        Log(message, LogLevel.Success);
    }

    public static void LogError(string message)
    {
        Log(message, LogLevel.Error);
    }

    public static void LogWarn(string message)
    {
        Log(message, LogLevel.Warn);
    }

    public static void Clear()
    {
        var window = Instance;
        window.Dispatcher.Invoke(() => window.LogBox.Document.Blocks.Clear());
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Document.Blocks.Clear();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextRange(LogBox.Document.ContentStart, LogBox.Document.ContentEnd).Text;
        Clipboard.SetText(text);
    }

    private void LogWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public void ShowLog()
    {
        Show();
        Activate();
        WindowState = WindowState.Normal;
    }
}

public enum LogLevel
{
    Info,
    Warn,
    Error,
    Success
}
