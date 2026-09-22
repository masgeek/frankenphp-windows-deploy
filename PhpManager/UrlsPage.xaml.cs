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
            "Delete ALL custom URLs and restore defaults?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "urls.db");
            if (System.IO.File.Exists(dbPath))
                System.IO.File.Delete(dbPath);

            UrlDatabase.SeedDefaults();
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
}
