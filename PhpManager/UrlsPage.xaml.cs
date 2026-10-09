using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class UrlsPage : UserControl
{
    private UrlRecord? _editingRecord;

    public UrlsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshAll();
    }

    private void RefreshAll()
    {
        LoadGrid();
        LoadCategoryComboBox();
        ClearForm();
    }

    private void LoadGrid()
    {
        UrlDatabase.Initialize();
        UrlsGrid.ItemsSource = null;
        UrlsGrid.ItemsSource = UrlDatabase.GetAll();
    }

    private void LoadCategoryComboBox()
    {
        var categories = UrlDatabase.GetCategories();
        CategoryBox.ItemsSource = categories;
    }

    private void ClearForm()
    {
        _editingRecord = null;
        FormTitle.Text = "New URL";
        CategoryBox.Text = "";
        KeyBox.Text = "";
        UrlTemplateBox.Text = "";
        DescriptionBox.Text = "";
        DefaultCheck.IsChecked = false;
        DeleteBtn.Visibility = Visibility.Collapsed;
    }

    private void LoadForm(UrlRecord record)
    {
        _editingRecord = record;
        FormTitle.Text = $"Editing: {record.Category} / {record.Key}";
        CategoryBox.Text = record.Category;
        KeyBox.Text = record.Key;
        UrlTemplateBox.Text = record.UrlTemplate;
        DescriptionBox.Text = record.Description;
        DefaultCheck.IsChecked = record.IsDefault;
        DeleteBtn.Visibility = Visibility.Visible;
    }

    private UrlRecord FormToRecord()
    {
        return new UrlRecord
        {
            Id = _editingRecord?.Id ?? 0,
            Category = CategoryBox.Text.Trim(),
            Key = KeyBox.Text.Trim(),
            UrlTemplate = UrlTemplateBox.Text.Trim(),
            Description = DescriptionBox.Text.Trim(),
            IsDefault = DefaultCheck.IsChecked == true
        };
    }

    private void UrlsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UrlsGrid.SelectedItem is UrlRecord record)
            LoadForm(record);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var record = FormToRecord();

        if (string.IsNullOrEmpty(record.Category) || string.IsNullOrEmpty(record.Key))
        {
            StatusBar.SetStatus("Category and Key are required.");
            return;
        }
        if (string.IsNullOrEmpty(record.UrlTemplate))
        {
            StatusBar.SetStatus("URL Template is required.");
            return;
        }

        try
        {
            if (record.Id > 0)
            {
                UrlDatabase.Update(record);
                StatusBar.SetStatus($"URL '{record.Category}/{record.Key}' updated.");
            }
            else
            {
                UrlDatabase.Insert(record);
                StatusBar.SetStatus($"URL '{record.Category}/{record.Key}' added.");
            }
            LoadGrid();
            LoadCategoryComboBox();
            ClearForm();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ClearForm();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingRecord == null) return;

        var result = MessageBox.Show(
            $"Delete URL '{_editingRecord.Category}/{_editingRecord.Key}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            UrlDatabase.Delete(_editingRecord.Id);
            StatusBar.SetStatus("URL deleted.");
            LoadGrid();
            ClearForm();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Delete ALL stored URLs and restore defaults?\n\nAny custom URLs will be lost.",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            UrlDatabase.ResetToDefaults();
            StatusBar.SetStatus("URLs reset to defaults.");
            LoadGrid();
            LoadCategoryComboBox();
            ClearForm();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private static string ExpandTokens(string template) => template
        .Replace("{0}", "8.4.0")
        .Replace("{version}", "latest")
        .Replace("{package}", "probe")
        .Replace("{path}", "latest/download");

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var target = _editingRecord ?? FormToRecord();
        var url = ExpandTokens(target.UrlTemplate);

        if (string.IsNullOrWhiteSpace(url))
        {
            StatusBar.SetStatus("Enter a URL template to test.");
            return;
        }

        TestBtn.IsEnabled = false;
        StatusBar.SetStatus($"Testing {url}...", true);
        try
        {
            var (ok, message) = await PhpService.TestUrlAsync(url, target.Category + "/" + target.Key);
            if (ok)
                StatusBar.SetSuccess(message);
            else
                StatusBar.SetStatus(message);
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }

    private async void TestAll_Click(object sender, RoutedEventArgs e)
    {
        TestAllBtn.IsEnabled = false;
        StatusBar.SetStatus("Testing all URLs...", true);
        try
        {
            var (passed, failed) = await PhpService.TestAllUrlsAsync(new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));

            var summary = $"Tested: {passed} reachable, {failed} failed.";
            if (failed == 0) StatusBar.SetSuccess(summary);
            else StatusBar.SetStatus(summary + " See the log for details.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            TestAllBtn.IsEnabled = true;
        }
    }

    private void CompatEdit_Click(object sender, RoutedEventArgs e)
    {
        var current = System.Text.Json.JsonSerializer.Serialize(
            XdebugCompat.Current,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var window = new CompatibilityEditorWindow(current)
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() == true)
        {
            try
            {
                var edited = System.Text.Json.JsonSerializer.Deserialize<XdebugCompat>(window.EditedJson);
                if (edited == null)
                {
                    StatusBar.SetStatus("Could not parse the compatibility JSON.");
                    return;
                }

                XdebugCompat.Save(edited);
                StatusBar.SetSuccess("Compatibility matrix saved.");
            }
            catch (Exception ex)
            {
                StatusBar.SetStatus($"Invalid JSON: {ex.Message}");
            }
        }
    }

    private void CompatReset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reset the Xdebug compatibility matrix to defaults?",
            "Reset Matrix", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={Path.Combine(AppContext.BaseDirectory, "urls.db")}");
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM app_config WHERE Key = 'xdebug.compatibility'";
            cmd.ExecuteNonQuery();

            StatusBar.SetSuccess("Compatibility matrix reset to defaults.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export URL configuration",
            FileName = "php-manager-urls.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, UrlDatabase.ExportJson(), new System.Text.UTF8Encoding(false));
            StatusBar.SetSuccess($"Exported {UrlDatabase.GetAll().Count} URL(s) to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import URL configuration",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        var overwrite = MessageBox.Show(
            "Overwrite URLs that already exist?\n\nYes = overwrite matching entries\nNo = only add new ones",
            "Import",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var count = UrlDatabase.ImportJson(json, overwrite);
            StatusBar.SetSuccess($"Imported {count} URL(s).");
            LoadGrid();
            LoadCategoryComboBox();
            ClearForm();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }
}
