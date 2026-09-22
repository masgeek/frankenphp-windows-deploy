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
        _pages["Servy"] = new ServyPage();
        _pages["Urls"] = new UrlsPage();
        _pages["Config"] = new ConfigPage();
        _pages["Cacert"] = new CacertPage();
        _pages["Settings"] = new SettingsPage();

        ContentArea.Children.Clear();
        ContentArea.Children.Add(_pages["Versions"]);

        HighlightNav(VersionsButton);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && _pages.TryGetValue(tag, out var page))
        {
            ContentArea.Children.Clear();
            ContentArea.Children.Add(page);
            HighlightNav(btn);
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
