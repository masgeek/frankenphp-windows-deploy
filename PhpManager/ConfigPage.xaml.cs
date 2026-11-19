using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class ConfigPage : UserControl
{
    private readonly Dictionary<string, ChoiceField> _inputs = new();
    private Dictionary<string, string?> _loaded = new();
    private bool _opcacheLoadedAtLoad;
    private string _version = "";

    public ConfigPage()
    {
        InitializeComponent();
        BuildSettingsUi();
        LaravelProfileCombo.SelectionChanged += (_, _) => LaravelDescText.Text = LaravelPreset.Describe(SelectedProfile());
        LaravelDescText.Text = LaravelPreset.Describe(SelectedProfile());
        Loaded += async (_, _) => await ReloadAsync();
    }

    // --- UI construction -------------------------------------------------------------------------

    private void BuildSettingsUi()
    {
        foreach (var group in IniSettingDefs.All.GroupBy(s => s.Group))
        {
            SettingsPanel.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#1F2937")!,
                Margin = new Thickness(0, 12, 0, 4)
            });

            foreach (var setting in group)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var label = new TextBlock
                {
                    Text = setting.Label,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = setting.Key,
                    Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#374151")!
                };
                Grid.SetColumn(label, 0);

                var field = new ChoiceField(setting);
                Grid.SetColumn(field.Root, 1);
                _inputs[setting.Key] = field;

                var hint = new TextBlock
                {
                    Text = string.IsNullOrEmpty(setting.Hint) ? $"Default: {setting.Default}" : $"Default: {setting.Default}. {setting.Hint}",
                    Style = (Style)FindResource("SubText"),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                };
                Grid.SetColumn(hint, 2);

                row.Children.Add(label);
                row.Children.Add(field.Root);
                row.Children.Add(hint);
                SettingsPanel.Children.Add(row);
            }
        }
    }

    // --- Loading ---------------------------------------------------------------------------------

    private async Task ReloadAsync()
    {
        _version = PhpService.GetActiveVersion();
        var hasIni = !string.IsNullOrEmpty(_version) && File.Exists(PhpService.IniPathFor(_version));
        SetSettingsEnabled(hasIni);

        if (string.IsNullOrEmpty(_version))
        {
            BackupList.ItemsSource = null;
            StatusBar.SetStatus("No active PHP version.");
            return;
        }

        if (!hasIni)
        {
            BackupList.ItemsSource = PhpService.ListIniBackups(_version);
            StatusBar.SetStatus($"PHP {_version} has no php.ini. Apply a Development or Production profile above.");
            return;
        }

        var version = _version;
        var (settings, opcache, backups) = await Task.Run(() =>
            (PhpService.ReadSettings(version), PhpService.IsOpcacheLoaded(version), PhpService.ListIniBackups(version)));

        _loaded = settings;
        _opcacheLoadedAtLoad = opcache;
        OpcacheLoadCheck.IsChecked = opcache;
        BackupList.ItemsSource = backups;

        foreach (var (key, field) in _inputs)
            field.SetValue(settings.GetValueOrDefault(key));

        StatusBar.SetStatus($"PHP {_version}: {settings.Count(kv => kv.Value != null)} common settings set, " +
                            $"OPcache {(opcache ? "loaded" : "not loaded")}, {backups.Count} backup(s).");
    }

    private void SetSettingsEnabled(bool enabled)
    {
        SettingsPanel.IsEnabled = enabled;
        OpcacheLoadCheck.IsEnabled = enabled;
        SaveSettingsBtn.IsEnabled = enabled;
        SetCustomBtn.IsEnabled = enabled;
        BackupNowBtn.IsEnabled = enabled;
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    // --- php.ini source (dev / prod) -------------------------------------------------------------

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var activeVersion = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
        {
            StatusBar.SetStatus("No active PHP version.");
            return;
        }

        try
        {
            if (DevRadio.IsChecked == true)
                StatusBar.SetStatus(PhpService.ApplyIniProfile(activeVersion, IniProfile.Development) + " Previous php.ini backed up.");
            else if (ProdRadio.IsChecked == true)
                StatusBar.SetStatus(PhpService.ApplyIniProfile(activeVersion, IniProfile.Production) + " Previous php.ini backed up.");
            else
                StatusBar.SetStatus("No changes applied.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
            return;
        }

        var message = StatusBar.CurrentText;
        await ReloadAsync();
        if (!string.IsNullOrEmpty(message))
            StatusBar.SetStatus(message);
    }

    // --- Common settings -------------------------------------------------------------------------

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_version))
            return;

        var changes = new Dictionary<string, string>();
        foreach (var setting in IniSettingDefs.All)
        {
            var text = _inputs[setting.Key].GetValue() ?? "";

            if (text.Length == 0)
                continue; // empty = leave as is

            var current = _loaded.GetValueOrDefault(setting.Key);
            if (current != null && string.Equals(current, text, StringComparison.OrdinalIgnoreCase))
                continue;

            var error = IniSettingDefs.Validate(setting, text);
            if (error != null)
            {
                StatusBar.SetStatus(error);
                _inputs[setting.Key].Root.Focus();
                return;
            }

            changes[setting.Key] = IniSettingDefs.Normalize(setting, text);
        }

        var wantOpcache = OpcacheLoadCheck.IsChecked == true;
        var opcacheChanged = wantOpcache != _opcacheLoadedAtLoad;

        if (changes.Count == 0 && !opcacheChanged)
        {
            StatusBar.SetStatus("Nothing to save.");
            return;
        }

        var warning = SizeWarning(changes);

        try
        {
            if (opcacheChanged)
                PhpService.SetOpcacheLoaded(_version, wantOpcache);
            if (changes.Count > 0)
                PhpService.SaveDirectives(_version, changes);
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
            return;
        }

        var parts = new List<string>();
        if (changes.Count > 0)
            parts.Add($"Saved {changes.Count} setting(s): {string.Join(", ", changes.Select(kv => $"{kv.Key}={kv.Value}"))}");
        if (opcacheChanged)
            parts.Add($"OPcache {(wantOpcache ? "loaded" : "unloaded")}");
        var summary = string.Join(". ", parts) + ". Backup taken; restart PHP for web requests." + (warning != null ? " " + warning : "") + RunningServicesNote();

        await ReloadAsync();
        StatusBar.SetStatus(summary);
    }

    /// <summary>upload_max_filesize larger than post_max_size can never be used: warn.</summary>
    private string? SizeWarning(Dictionary<string, string> changes)
    {
        string? Effective(string key) => changes.GetValueOrDefault(key) ?? _loaded.GetValueOrDefault(key);

        var upload = IniSettingDefs.ToBytes(Effective("upload_max_filesize") ?? "2M");
        var post = IniSettingDefs.ToBytes(Effective("post_max_size") ?? "8M");
        return upload != null && post != null && post != IniSettingDefs.Unlimited && upload > post
            ? "Warning: upload_max_filesize is larger than post_max_size, so big uploads will still fail."
            : null;
    }

    private async void SetCustom_Click(object sender, RoutedEventArgs e)
    {
        var key = CustomKeyBox.Text.Trim();
        var value = CustomValueBox.Text.Trim();

        if (!IniSettingDefs.IsValidKey(key))
        {
            StatusBar.SetStatus("Directive name may only contain letters, digits, '_' and '.'.");
            return;
        }

        if (value.Length == 0 || value.Contains('\n') || value.Contains('\r'))
        {
            StatusBar.SetStatus("Enter a single-line value.");
            return;
        }

        var known = IniSettingDefs.Find(key);
        if (known != null)
        {
            var error = IniSettingDefs.Validate(known, value);
            if (error != null)
            {
                StatusBar.SetStatus(error);
                return;
            }

            value = IniSettingDefs.Normalize(known, value);
        }

        try
        {
            PhpService.SaveDirectives(_version, new Dictionary<string, string> { [key] = value });
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
            return;
        }

        CustomKeyBox.Clear();
        CustomValueBox.Clear();
        await ReloadAsync();
        StatusBar.SetStatus($"Set {key} = {value}. Backup taken.");
    }

    // --- Laravel preset ----------------------------------------------------------------------------

    private LaravelProfile SelectedProfile() =>
        LaravelProfileCombo.SelectedIndex == 1 ? LaravelProfile.Production : LaravelProfile.Local;

    private async void LaravelPreset_Click(object sender, RoutedEventArgs e)
    {
        var version = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(version))
        {
            StatusBar.SetStatus("No active PHP version.");
            return;
        }

        var profile = SelectedProfile();
        var answer = MessageBox.Show(
            $"Apply the Laravel ({(profile == LaravelProfile.Local ? "local development" : "production")}) preset to PHP {version}?{Environment.NewLine}{Environment.NewLine}" +
            $"{LaravelPreset.Describe(profile)}{Environment.NewLine}{Environment.NewLine}php.ini is backed up first, so you can roll back.",
            "Laravel preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        LaravelPresetBtn.IsEnabled = false;
        try
        {
            var message = await Task.Run(() => FeatureService.ApplyLaravelPreset(version, profile));
            await ReloadAsync();
            StatusBar.SetStatus(message + RunningServicesNote());
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            LaravelPresetBtn.IsEnabled = true;
        }
    }

    /// <summary>FrankenPHP services read php.ini only at start, so running ones need a restart.</summary>
    private static string RunningServicesNote()
    {
        try
        {
            var running = FeatureService.RunningFrankenServices();
            return running.Count == 0 ? "" : $" Restart FrankenPHP service(s) to apply: {string.Join(", ", running)}.";
        }
        catch
        {
            return "";
        }
    }

    // --- Backups / rollback ----------------------------------------------------------------------

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_version) || BackupList.SelectedItem is not PhpIniBackup.BackupInfo backup)
        {
            StatusBar.SetStatus("Select a backup to restore.");
            return;
        }

        var answer = MessageBox.Show(
            $"Restore php.ini from {backup.Created:yyyy-MM-dd HH:mm:ss}?\n\nThe current php.ini is backed up first, so you can undo this.",
            "Restore php.ini", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            PhpService.RestoreIniBackup(_version, backup.Path);
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
            return;
        }

        await ReloadAsync();
        StatusBar.SetStatus($"Restored php.ini from {backup.Created:yyyy-MM-dd HH:mm:ss}. Restart PHP for web requests.");
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_version) || BackupList.SelectedItem is not PhpIniBackup.BackupInfo backup)
        {
            StatusBar.SetStatus("Select a backup to compare.");
            return;
        }

        try
        {
            var current = File.Exists(PhpService.IniPathFor(_version)) ? File.ReadAllText(PhpService.IniPathFor(_version)) : "";
            var window = new DiffWindow($"{backup.Created:yyyy-MM-dd HH:mm:ss}", File.ReadAllText(backup.Path), current)
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_version))
            return;

        try
        {
            var before = PhpService.ListIniBackups(_version).Count;
            PhpService.BackupIniNow(_version);
            await ReloadAsync();
            var created = PhpService.ListIniBackups(_version).Count > before;
            StatusBar.SetStatus(created ? "Backup created." : "php.ini is unchanged since the last backup; nothing new to save.");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_version))
            return;

        var folder = PhpIniBackup.FolderFor(PhpService.IniPathFor(_version));
        if (!Directory.Exists(folder))
        {
            StatusBar.SetStatus("No backups yet.");
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}

/// <summary>
/// A dropdown of preset values. Where a free value is also allowed it has a "Custom..." entry that reveals a text box,
/// so typing is only needed for values outside the presets.
/// </summary>
internal sealed class ChoiceField
{
    private const string Keep = "(keep current)";
    private const string Custom = "Custom...";

    private readonly IniSetting _setting;
    private readonly ComboBox _combo = new() { Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _custom = new() { Padding = new Thickness(4), Margin = new Thickness(0, 4, 8, 0), Visibility = Visibility.Collapsed };

    public StackPanel Root { get; } = new();

    public ChoiceField(IniSetting setting)
    {
        _setting = setting;
        _combo.ToolTip = setting.Key;
        _custom.ToolTip = $"Value for {setting.Key}";

        _combo.Items.Add(Keep);
        foreach (var option in setting.Options)
            _combo.Items.Add(option);
        if (setting.CanBeCustom)
            _combo.Items.Add(Custom);

        _combo.SelectedIndex = 0;
        _combo.SelectionChanged += (_, _) =>
            _custom.Visibility = _combo.SelectedItem as string == Custom ? Visibility.Visible : Visibility.Collapsed;

        Root.Children.Add(_combo);
        Root.Children.Add(_custom);
    }

    /// <summary>The chosen value, or null when the setting should be left as is.</summary>
    public string? GetValue()
    {
        var selected = _combo.SelectedItem as string;
        if (selected == null || selected == Keep)
            return null;
        return selected == Custom ? _custom.Text.Trim() : selected;
    }

    public void SetValue(string? value)
    {
        _custom.Clear();

        if (value == null)
        {
            _combo.SelectedItem = Keep;
            return;
        }

        if (_setting.Kind == IniValueKind.Bool)
            value = value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                ? "On"
                : "Off";

        var match = _setting.Options.FirstOrDefault(o => o.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            _combo.SelectedItem = match;
        }
        else if (_setting.CanBeCustom)
        {
            // a value outside the presets: show it in the custom box rather than hiding it
            _combo.SelectedItem = Custom;
            _custom.Text = value;
        }
        else
        {
            // fixed list but php.ini holds something else: add it so the real value stays visible
            _combo.Items.Insert(1, value);
            _combo.SelectedItem = value;
        }
    }
}
