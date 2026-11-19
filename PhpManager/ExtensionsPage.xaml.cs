using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace PhpManager;

public partial class ExtensionsPage : UserControl
{
    private readonly ObservableCollection<ExtensionInfo> _extensions = new();

    public ExtensionsPage()
    {
        InitializeComponent();
        // Grouped by function (Database, Caching, ...) and sorted within each group.
        var view = new System.Windows.Data.ListCollectionView(_extensions);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ExtensionInfo.Group)));
        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(ExtensionInfo.GroupOrder), System.ComponentModel.ListSortDirection.Ascending));
        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(ExtensionInfo.Name), System.ComponentModel.ListSortDirection.Ascending));
        ExtensionGrid.ItemsSource = view;
        Loaded += async (_, _) => await LoadExtensionsAsync();
    }

    private async Task LoadExtensionsAsync()
    {
        StatusBar.SetStatus("Loading extensions...", true);

        // php -m runs php.exe to discover the built-in extensions; keep that off the UI thread.
        var list = await Task.Run(() => PhpService.GetExtensions());

        _extensions.Clear();
        foreach (var ext in list)
            _extensions.Add(ext);

        var active = PhpService.GetActiveVersion();
        var enabled = list.Count(x => x.Enabled);
        StatusBar.SetStatus($"{list.Count} extensions for PHP {(string.IsNullOrEmpty(active) ? "(none)" : active)}: " +
                            $"{enabled} enabled, {list.Count - enabled} disabled.");
    }

    private async void LoadExtensions_Click(object sender, System.Windows.RoutedEventArgs e) => await LoadExtensionsAsync();

    /// <summary>Ticks every Laravel extension in the grid so one Save enables them all.</summary>
    private void SelectLaravel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var items = _extensions.ToList();
        var changed = 0;
        foreach (var ext in items.Where(x => LaravelExtensions.IsLaravel(x.Name) && !x.Enabled))
        {
            ext.Enabled = true;
            changed++;
        }

        // ExtensionInfo isn't observable, so re-add the rows to refresh the checkboxes.
        _extensions.Clear();
        foreach (var ext in items)
            _extensions.Add(ext);

        var missing = LaravelExtensions.Names
            .Where(n => !items.Any(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        StatusBar.SetStatus($"Selected {changed} more Laravel extension(s). Click Save Changes to apply." +
                            (missing.Count > 0 ? $" Not in php.ini: {string.Join(", ", missing)}." : ""));
    }

    private async void SaveExtensions_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var activeVersion = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
        {
            StatusBar.SetStatus("No active PHP version. Install and set one first.");
            return;
        }

        // A checkbox ticked a moment ago may still be in edit mode; commit it so the item holds the new value.
        ExtensionGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ExtensionGrid.CommitEdit();

        var snapshot = _extensions.ToList();
        SaveBtn.IsEnabled = false;
        try
        {
            var pending = await Task.Run(() => PhpService.PendingExtensionChanges(activeVersion, snapshot));
            if (pending.Count == 0)
            {
                StatusBar.SetStatus("No changes to save: the list matches php.ini.");
                return;
            }

            await Task.Run(() => PhpService.SaveExtensions(activeVersion, snapshot));

            // Read php.ini back: anything still different was not written.
            var stillDifferent = await Task.Run(() => PhpService.PendingExtensionChanges(activeVersion, snapshot));
            var summary = string.Join(", ", pending.Select(x => $"{(x.Enabled ? "enabled" : "disabled")} {x.Name}"));

            await LoadExtensionsAsync();
            StatusBar.SetStatus(stillDifferent.Count == 0
                ? $"Saved to php.ini (backup taken): {summary}. Restart PHP for web requests."
                : $"Saved, but php.ini still differs for: {string.Join(", ", stillDifferent.Select(x => x.Name))}.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            SaveBtn.IsEnabled = true;
        }
    }

    private async void Verify_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var activeVersion = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
        {
            StatusBar.SetStatus("No active PHP version.");
            return;
        }

        VerifyBtn.IsEnabled = false;
        StatusBar.SetStatus("Verifying extensions...", true);
        try
        {
            var result = await Task.Run(() => PhpService.VerifyExtensions(activeVersion));
            var (loaded, failed, noDll) = (result.Loaded, result.Failed, result.NoDll);

            var phpVersion = await Task.Run(() => PhpService.GetPhpVersionString(activeVersion));
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(phpVersion))
                lines.Add(phpVersion);

            if (result.TimedOut)
                lines.Add("php -m timed out and was stopped");
            if (loaded.Count > 0)
                lines.Add($"Loaded ({loaded.Count}): {string.Join(", ", loaded)}");
            if (failed.Count > 0)
                lines.Add($"Failed ({failed.Count}): {string.Join(", ", failed)}");
            if (noDll.Count > 0)
                lines.Add($"No DLL ({noDll.Count}): {string.Join(", ", noDll)}");

            if (loaded.Count + failed.Count + noDll.Count == 0)
                lines.Add("No extensions enabled in php.ini.");

            StatusBar.SetStatus(string.Join(" | ", lines));

            // Don't just report problems: explain each one and offer to resolve them.
            var problems = failed.Concat(noDll).ToList();
            if (problems.Count > 0)
            {
                var detail = string.Join(Environment.NewLine,
                    problems.Select(p => $"  - {p}: {result.Reasons.GetValueOrDefault(p, "unknown reason")}"));
                var answer = System.Windows.MessageBox.Show(
                    $"{problems.Count} enabled extension(s) will not load:{Environment.NewLine}{Environment.NewLine}" +
                    $"{detail}{Environment.NewLine}{Environment.NewLine}" +
                    "Disable them in php.ini so PHP starts cleanly?",
                    "Extension problems", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

                if (answer == System.Windows.MessageBoxResult.Yes)
                {
                    await Task.Run(() => PhpService.DisableExtensions(activeVersion, problems));
                    await LoadExtensionsAsync();
                    StatusBar.SetStatus($"Disabled {problems.Count} problem extension(s): {string.Join(", ", problems)}. Run Verify again to confirm.");
                }
            }
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            VerifyBtn.IsEnabled = true;
        }
    }
}
