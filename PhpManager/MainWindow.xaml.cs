using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PhpManager;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();
    private Button? _activeNavButton;

    public MainWindow()
    {
        InitializeComponent();
        _pages["Versions"] = new VersionsPage();
        _pages["Extensions"] = new ExtensionsPage();
        _pages["Redis"] = new RedisPage();
        _pages["SqlServer"] = new SqlServerPage();
        _pages["FrankenPhp"] = new FrankenPhpPage();
        _pages["Xdebug"] = new XdebugPage();
        _pages["Servy"] = new ServyPage();
        _pages["Urls"] = new UrlsPage();
        _pages["Config"] = new ConfigPage();
        _pages["Cacert"] = new CacertPage();
        _pages["Settings"] = new SettingsPage();
        _pages["Doctor"] = new DoctorPage();
        _pages["Project"] = new ProjectPage();
        _pages["Composer"] = new ComposerPage();

        ContentArea.Children.Clear();
        ContentArea.Children.Add(_pages["Versions"]);

        HighlightNav(VersionsButton);

        PhpService.ActiveVersionChanged += () => Dispatcher.InvokeAsync(RefreshActiveStrip);
        Loaded += (_, _) => RefreshActiveStrip();
    }

    /// <summary>Shows which PHP the PHP-level pages act on; the real version is read off the UI thread.</summary>
    private async void RefreshActiveStrip()
    {
        try
        {
            var (name, dir, real) = await Task.Run(() =>
            {
                var active = PhpService.GetActiveVersion();
                if (string.IsNullOrEmpty(active))
                    return ("", "", (string?)null);

                var installDir = active == "frankenphp" ? PhpService.FrankenPhpPath : System.IO.Path.Combine(PhpService.BasePath, active);
                var version = PhpInstallationCache.GetVersion(PhpService.BasePath, installDir, PhpPathDetector.ReadVersion);
                return (active == "frankenphp" ? "FrankenPHP" : active, installDir, version);
            });

            if (name == "")
            {
                ActiveVersionText.Text = "none selected";
                ActivePathText.Text = "Install a version on the Versions page and set it active.";
                return;
            }

            ActiveVersionText.Text = real != null && real != name ? $"{name}  (PHP {real})" : name;
            ActivePathText.Text = dir;
        }
        catch
        {
            ActiveVersionText.Text = "unknown";
            ActivePathText.Text = "";
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && _pages.TryGetValue(tag, out var page))
        {
            ContentArea.Children.Clear();
            ContentArea.Children.Add(page);
            HighlightNav(btn);
            RefreshActiveStrip();
        }
    }

    private void HighlightNav(Button button)
    {
        if (_activeNavButton != null)
            _activeNavButton.Background = Brushes.Transparent;

        _activeNavButton = button;
        _activeNavButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8E8E8"));
    }
}
