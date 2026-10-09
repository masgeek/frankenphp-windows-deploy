using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PhpManager;

public partial class XdebugPage : UserControl
{
    private string _activeVersion = "";
    private XdebugEnvironment? _env;
    private XdebugVerdict? _verdict;

    public XdebugPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadActiveConfig();
            DetectActive();
        };
    }

    private string ActiveVersion
    {
        get
        {
            var v = PhpService.GetActiveVersion();
            return v == "frankenphp" ? "" : v;
        }
    }

    // ---------- Step 1: Detect ----------

    private void DetectActive()
    {
        _activeVersion = ActiveVersion;
        if (string.IsNullOrEmpty(_activeVersion))
        {
            Reset("No active PHP version. Select a PHP version on the Versions page first.");
            return;
        }

        try
        {
            ShowDetection(PhpService.DetectXdebugEnvironment(_activeVersion));
        }
        catch (Exception ex)
        {
            Reset($"Detection failed: {ex.Message}");
        }
    }

    private void Detect_Click(object sender, RoutedEventArgs e) => DetectActive();

    private async void RefreshVersions_Click(object sender, RoutedEventArgs e)
    {
        RefreshVersionsBtn.IsEnabled = false;
        StatusBar.SetStatus("Fetching available Xdebug versions...", true);
        try
        {
            var versions = await PhpService.GetAvailableExtensionVersionsAsync(
                Urls.Xdebug.VersionIndex,
                new Progress<string>(msg => Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));

            if (versions.Count == 0)
            {
                StatusBar.SetStatus("No versions found. Check the Xdebug version index URL.");
                return;
            }

            var selected = VersionBox.Text;
            VersionBox.ItemsSource = versions;
            VersionBox.Text = versions.Contains(selected) ? selected : versions[0];

            StatusBar.SetSuccess($"Found {versions.Count} Xdebug version(s).");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            RefreshVersionsBtn.IsEnabled = true;
        }
    }

    private void Parse_Click(object sender, RoutedEventArgs e)
    {
        var raw = RawInfoBox.Text;
        if (string.IsNullOrWhiteSpace(raw))
        {
            StatusBar.SetStatus("Paste phpinfo() or php -i output first.");
            return;
        }

        var env = XdebugCompatibility.Parse(raw);
        if (string.IsNullOrEmpty(env.PhpVersion))
        {
            StatusBar.SetStatus("Could not find a PHP version in the pasted output.");
            return;
        }

        ShowDetection(env);
        StatusBar.SetStatus("Analysed pasted output. Downloading still targets the active PHP version.");
    }

    private void ShowDetection(XdebugEnvironment env)
    {
        _env = env;

        DetectionPanel.Visibility = Visibility.Visible;
        DetPhpVersion.Text = env.PhpVersion;
        DetThreadSafety.Text = env.ThreadSafety.ToUpperInvariant();
        DetArchitecture.Text = env.Architecture;
        DetCompiler.Text = env.WinCompiler > 0 ? env.Compiler : "unknown";
        DetDebug.Text = env.DebugBuild ? "yes" : "no";
        DetConfigFile.Text = string.IsNullOrEmpty(env.ConfigFile) ? "(none)" : env.ConfigFile;
        DetExtensionDir.Text = string.IsNullOrEmpty(env.ExtensionDir) ? "(none)" : env.ExtensionDir;
        DetLoaded.Text = env.XdebugLoaded
            ? $"yes (v{env.LoadedXdebugVersion})"
            : "no";

        // ---------- Step 2: Validate ----------
        _verdict = XdebugCompatibility.Evaluate(env);

        ValidateCard.Visibility = Visibility.Visible;
        ActionCard.Visibility = Visibility.Visible;

        if (_verdict.Supported)
        {
            VerdictBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
            VerdictText.Text = _verdict.Message;
            LogWindow.LogSuccess(_verdict.Message);

            if (string.IsNullOrWhiteSpace(VersionBox.Text))
                VersionBox.Text = _verdict.RecommendedVersion;

            var zipName = XdebugCompatibility.BuildZipName(env, _verdict.RecommendedVersion);
            DownloadPanel.Visibility = Visibility.Visible;
            ZipNameText.Text = $"{zipName}.zip";
            DllNameText.Text = XdebugCompatibility.BuildExpectedDllName(env, _verdict.RecommendedVersion);
            CanonicalText.Text = "php_xdebug.dll";

            StatusBar.SetSuccess($"Ready to install Xdebug {_verdict.RecommendedVersion} for PHP {env.PhpVersion}.");
        }
        else
        {
            VerdictBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            VerdictText.Text = _verdict.Message;
            DownloadPanel.Visibility = Visibility.Collapsed;
            LogWindow.LogError(_verdict.Message);
            StatusBar.SetStatus(_verdict.Message);
        }
    }

    private void Reset(string message)
    {
        DetectionPanel.Visibility = Visibility.Collapsed;
        ValidateCard.Visibility = Visibility.Collapsed;
        ActionCard.Visibility = Visibility.Collapsed;
        StatusBar.SetStatus(message);
    }

    // ---------- Step 3: Download ----------

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var version = VersionBox.Text.Trim();
        if (string.IsNullOrEmpty(_activeVersion))
        {
            StatusBar.SetStatus("No active PHP version.");
            return;
        }
        if (string.IsNullOrEmpty(version))
        {
            StatusBar.SetStatus("Enter an Xdebug version.");
            return;
        }

        DownloadBtn.IsEnabled = false;
        ApplyBtn.IsEnabled = false;
        StatusBar.SetStatus($"Downloading Xdebug {version}...", true);
        try
        {
            await PhpService.InstallXdebugAsync(_activeVersion, version, new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusBar.SetStatus(msg, true))));
            StatusBar.SetStatus($"Xdebug {version} installed as php_xdebug.dll. Apply settings when ready.");
            ShowInstalledState();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            DownloadBtn.IsEnabled = true;
            ApplyBtn.IsEnabled = true;
        }
    }

    private void ShowInstalledState()
    {
        if (string.IsNullOrEmpty(_activeVersion)) return;

        var installed = PhpService.IsXdebugInstalled(_activeVersion);
        var loaded = PhpService.VerifyXdebug(_activeVersion);

        VerifyBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            loaded ? "#16A34A" : installed ? "#D97706" : "#DC2626"));

        VerifyText.Text = loaded
            ? "Xdebug is loaded and active."
            : installed
                ? "php_xdebug.dll is installed but not loaded yet. Apply the ini settings and restart PHP."
                : "php_xdebug.dll is not installed.";
    }

    // ---------- Step 4: Configure ----------

    private void LoadActiveConfig()
    {
        EnabledCheck.IsChecked = false;
        SelectComboValue(ModeBox, "debug");
        HostBox.Text = "127.0.0.1";
        PortBox.Text = "9003";
        IdeKeyBox.Text = "phpstorm";
        SelectComboValue(StartCombo, "trigger");
        SetFieldsEnabled(false);

        _activeVersion = ActiveVersion;
        if (string.IsNullOrEmpty(_activeVersion))
            return;

        var config = PhpService.ReadXdebugConfig(_activeVersion);
        EnabledCheck.IsChecked = config.Enabled;
        SelectComboValue(ModeBox, config.Mode);
        HostBox.Text = config.ClientHost;
        PortBox.Text = config.ClientPort.ToString();
        IdeKeyBox.Text = config.IdeKey;
        SelectComboValue(StartCombo, config.StartWithRequest);
        SetFieldsEnabled(config.Enabled);
        ShowInstalledState();
    }

    private void SetFieldsEnabled(bool enabled)
    {
        ModeBox.IsEnabled = enabled;
        HostBox.IsEnabled = enabled;
        PortBox.IsEnabled = enabled;
        IdeKeyBox.IsEnabled = enabled;
        StartCombo.IsEnabled = enabled;
    }

    private void Enabled_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = EnabledCheck.IsChecked == true;
        SetFieldsEnabled(enabled);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeVersion))
        {
            StatusBar.SetStatus("No active PHP version.");
            return;
        }

        var config = new XdebugConfig
        {
            Enabled = EnabledCheck.IsChecked == true,
            Mode = ComboValue(ModeBox),
            StartWithRequest = ComboValue(StartCombo),
            ClientHost = HostBox.Text.Trim(),
            ClientPort = int.TryParse(PortBox.Text.Trim(), out var port) ? port : 9003,
            IdeKey = IdeKeyBox.Text.Trim()
        };

        try
        {
            PhpService.ConfigureXdebug(_activeVersion, config);
            StatusBar.SetStatus("php.ini updated. Restart PHP to load Xdebug.");
            ShowInstalledState();
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }

    // ---------- Step 5: Verify ----------

    private void Verify_Click(object sender, RoutedEventArgs e) => ShowInstalledState();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(BuildIniSnippet());
        StatusBar.SetStatus("ini snippet copied to clipboard.");
    }

    private string BuildIniSnippet()
    {
        var lines = new List<string> { "[xdebug]" };
        lines.Add(EnabledCheck.IsChecked == true
            ? "zend_extension=xdebug"
            : ";zend_extension=xdebug");
        lines.Add($"xdebug.mode={ComboValue(ModeBox)}");
        lines.Add($"xdebug.start_with_request={ComboValue(StartCombo)}");
        lines.Add($"xdebug.client_host={HostBox.Text.Trim()}");
        lines.Add($"xdebug.client_port={(int.TryParse(PortBox.Text.Trim(), out var p) ? p : 9003)}");
        lines.Add($"xdebug.idekey={IdeKeyBox.Text.Trim()}");
        return string.Join("\n", lines);
    }

    // ---------- helpers ----------

    private static void SelectComboValue(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Content?.ToString() == value)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Content?.ToString() == part);
            if (match != null)
            {
                combo.SelectedItem = match;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private static string ComboValue(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
}