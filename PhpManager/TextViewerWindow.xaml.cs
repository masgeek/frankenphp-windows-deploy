using System.Windows;

namespace PhpManager;

/// <summary>Read-only text window that can reload itself from a provider (used for service logs).</summary>
public partial class TextViewerWindow : Window
{
    private readonly Func<string> _load;

    public TextViewerWindow(string title, Func<string> load)
    {
        InitializeComponent();
        Title = title;
        _load = load;
        Reload();
    }

    private void Reload()
    {
        ContentBox.Text = _load();
        ContentBox.ScrollToEnd();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
