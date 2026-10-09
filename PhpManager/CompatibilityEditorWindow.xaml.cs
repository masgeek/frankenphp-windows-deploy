using System.Windows;

namespace PhpManager;

public partial class CompatibilityEditorWindow : Window
{
    public string EditedJson => JsonBox.Text;

    public CompatibilityEditorWindow(string json)
    {
        InitializeComponent();
        JsonBox.Text = json;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Text.Json.JsonSerializer.Deserialize<XdebugCompat>(JsonBox.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Invalid JSON: {ex.Message}", "Validation", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}