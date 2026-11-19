using System.Windows;

namespace PhpManager;

public partial class DiffWindow : Window
{
    /// <summary>Shows what Restore would change: red lines are in the current file, green lines come from the backup.</summary>
    public DiffWindow(string backupLabel, string backupText, string currentText)
    {
        InitializeComponent();

        // old = current, new = backup: what restoring would do to the current file
        var full = LineDiff.Compute(currentText, backupText);
        var view = LineDiff.WithContext(full, 2);

        if (!LineDiff.HasChanges(full))
        {
            HeaderText.Text = $"The backup from {backupLabel} is identical to the current php.ini.";
            DiffList.ItemsSource = null;
            return;
        }

        var added = full.Count(l => l.Kind == DiffKind.Added);
        var removed = full.Count(l => l.Kind == DiffKind.Removed);
        HeaderText.Text = $"Restoring the backup from {backupLabel} would remove {removed} line(s) (red) and add {added} line(s) (green).";
        DiffList.ItemsSource = view;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
