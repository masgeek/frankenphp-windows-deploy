using System.IO;
using System.Text;

namespace PhpManager;

/// <summary>Orchestration for the health check, Laravel preset, upgrades, settings bundles and imports.</summary>
public static class FeatureService
{
    private static string InstallDir(string version) =>
        version == "frankenphp" ? PhpService.FrankenPhpPath : Path.Combine(PhpService.BasePath, version);

    // --- 1. Doctor -------------------------------------------------------------------------------

    public static List<DoctorResult> RunDoctor()
    {
        var results = new List<DoctorResult>();
        var active = PhpService.GetActiveVersion();

        if (string.IsNullOrEmpty(active))
        {
            results.Add(new("Active PHP", DoctorStatus.Fail, "No PHP version is active. Install one on the Versions page and set it active.", "set-active"));
        }
        else
        {
            var dir = InstallDir(active);
            var phpExe = Path.Combine(dir, "php.exe");
            var phpIni = Path.Combine(dir, "php.ini");

            if (!File.Exists(phpExe))
            {
                results.Add(new("Active PHP", DoctorStatus.Fail, $"php.exe is missing from {dir}."));
            }
            else
            {
                var run = ProcessRunner.Run(phpExe, "--version", dir, timeoutMs: 10_000);
                var parsed = PhpVersionParser.Parse(run.Output);
                if (run.TimedOut)
                    results.Add(new("Active PHP", DoctorStatus.Fail, "php.exe did not respond within 10 seconds."));
                else if (parsed == null)
                    results.Add(new("Active PHP", DoctorStatus.Fail, $"php.exe ran but printed no version. Output: {Shorten(run.Combined)}"));
                else
                {
                    var versionText = $"{parsed.Value.Major}.{parsed.Value.Minor}.{parsed.Value.Patch}";
                    results.Add(new("Active PHP", DoctorStatus.Pass, $"PHP {versionText} at {dir}."));
                    results.Add(DoctorEvaluators.PhpSupportCheck(versionText, DateOnly.FromDateTime(DateTime.Today)));
                }
            }

            if (!File.Exists(phpIni))
            {
                results.Add(new("php.ini", DoctorStatus.Fail, "No php.ini; PHP runs with built-in defaults and no extensions.", "apply-profile"));
            }
            else
            {
                var content = File.ReadAllText(phpIni);
                results.Add(new("php.ini", DoctorStatus.Pass, $"Found at {phpIni}."));
                results.Add(DoctorEvaluators.ExtensionDirCheck(content, dir, Directory.Exists));
                results.Add(DoctorEvaluators.CaCertCheck(content, File.Exists));

                if (File.Exists(phpExe))
                {
                    var verify = PhpService.VerifyExtensions(active);
                    var broken = verify.Failed.Concat(verify.NoDll).ToList();
                    results.Add(broken.Count == 0
                        ? new("Extensions", DoctorStatus.Pass, $"All {verify.Loaded.Count} enabled extensions load.")
                        : new("Extensions", DoctorStatus.Fail,
                            $"{broken.Count} enabled extension(s) will not load: " +
                            string.Join("; ", broken.Select(b => $"{b} ({verify.Reasons.GetValueOrDefault(b, "unknown")})")),
                            "disable-broken"));
                }
            }

            results.Add(VcRuntimeCheck());
        }

        var pathDirs = DoctorPathDirs();
        results.Add(DoctorEvaluators.PathCheck(pathDirs, string.IsNullOrEmpty(active) ? null : InstallDir(active),
            d => File.Exists(Path.Combine(d, "php.exe"))));
        results.Add(DoctorEvaluators.ComposerCheck(pathDirs, File.Exists));
        return results;
    }

    private static IEnumerable<string> DoctorPathDirs() =>
        PhpPathDetector.EnumerateDirs(
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));

    private static DoctorResult VcRuntimeCheck()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            if (key?.GetValue("Installed") is int installed && installed == 1)
                return new("Visual C++ runtime", DoctorStatus.Pass, $"Installed (version {key.GetValue("Version")}).");
        }
        catch
        {
            // fall through to the warning
        }

        return new("Visual C++ runtime", DoctorStatus.Warn,
            "The Visual C++ 2015-2022 x64 runtime was not detected. PHP and many extensions need it (download vc_redist.x64.exe from Microsoft).");
    }

    /// <summary>Runs the automatic fix attached to a result. Returns a message for the status bar.</summary>
    public static async Task<string> ApplyFixAsync(string fixId, IProgress<string>? progress)
    {
        var active = PhpService.GetActiveVersion();
        switch (fixId)
        {
            case "cacert":
                await PhpService.InstallCacertAsync(active, progress);
                return "CA certificate installed and configured.";

            case "disable-broken":
            {
                var verify = PhpService.VerifyExtensions(active);
                var broken = verify.Failed.Concat(verify.NoDll).ToList();
                PhpService.DisableExtensions(active, broken);
                return $"Disabled {broken.Count} broken extension(s): {string.Join(", ", broken)}. A backup of php.ini was taken.";
            }

            case "reapply-path":
                if (string.IsNullOrEmpty(active))
                    return "Set a PHP version active first.";
                PhpService.SetActiveVersion(active);
                return "PATH updated for the active version. Open a new terminal to pick it up (system PATH changes need the Versions page checkbox and admin).";

            case "install-composer":
                await ComposerService.InstallAsync(progress);
                return "Composer installed.";

            case "apply-profile":
                if (string.IsNullOrEmpty(active))
                    return "Set a PHP version active first.";
                var phpIni = PhpService.IniPathFor(active);
                if (File.Exists(phpIni))
                {
                    var text = File.ReadAllText(phpIni);
                    if (!text.Contains("extension_dir", StringComparison.OrdinalIgnoreCase) || DoctorEvaluators.ExtensionDirCheck(text, InstallDir(active), Directory.Exists).Status == DoctorStatus.Fail)
                    {
                        PhpService.PatchExtensionDir(phpIni, InstallDir(active));
                        return "extension_dir corrected (backup taken).";
                    }
                }

                return PhpService.ApplyIniProfile(active, IniProfile.Development);

            default:
                return "No automatic fix for this item.";
        }
    }

    private static string Shorten(string text) => text.Length <= 200 ? text.Trim() : text[..200].Trim() + "...";

    // --- 6. Laravel preset -----------------------------------------------------------------------

    /// <summary>
    /// One backup, one write: recommended directives, OPcache loaded, and the Laravel extensions whose DLLs exist.
    /// Creates php.ini from the matching template first when there is none.
    /// </summary>
    public static string ApplyLaravelPreset(string version, LaravelProfile profile)
    {
        var ini = PhpService.IniPathFor(version);
        if (!File.Exists(ini))
            PhpService.ApplyIniProfile(version, profile == LaravelProfile.Local ? IniProfile.Development : IniProfile.Production);

        var content = File.ReadAllText(ini);
        var applied = 0;
        foreach (var (key, value) in LaravelPreset.Settings(profile))
        {
            // keep a timezone you chose yourself
            if (key == "date.timezone" && !string.IsNullOrWhiteSpace(PhpIniEditor.GetDirective(content, key)))
                continue;

            content = PhpIniEditor.SetDirective(content, key, value);
            applied++;
        }

        var extDir = Path.Combine(InstallDir(version), "ext");
        var available = LaravelExtensions.Names.Where(n => File.Exists(Path.Combine(extDir, $"php_{n}.dll"))).ToList();
        content = LaravelExtensions.Enable(content, available);
        content = PhpIniEditor.SetExtensionState(content, "opcache", enabled: File.Exists(Path.Combine(extDir, "php_opcache.dll")), zend: true);

        PhpIniBackup.Write(ini, content);
        return $"Laravel ({profile}) preset applied: {applied} settings, {available.Count} extensions enabled. Backup taken; restart PHP for web requests.";
    }

    // --- 4. Upgrade ------------------------------------------------------------------------------

    /// <summary>
    /// Installs <paramref name="newVersion"/> next to <paramref name="oldVersion"/>, carries over php.ini and any extra
    /// extension DLLs, and makes it active if the old one was. The old version is kept so you can switch back.
    /// </summary>
    public static async Task<string> UpgradeAsync(string oldVersion, string newVersion, IProgress<string>? progress)
    {
        var wasActive = PhpService.GetActiveVersion() == oldVersion;
        await PhpService.InstallVersionAsync(newVersion, progress);

        var oldDir = InstallDir(oldVersion);
        var newDir = InstallDir(newVersion);

        progress?.Report("Carrying over extensions...");
        var oldExt = Path.Combine(oldDir, "ext");
        var newExt = Path.Combine(newDir, "ext");
        var copied = UpdateChecker.MissingExtensionFiles(oldExt, newExt);
        foreach (var file in copied)
            File.Copy(Path.Combine(oldExt, file), Path.Combine(newExt, file), overwrite: false);

        var oldIni = Path.Combine(oldDir, "php.ini");
        var newIni = Path.Combine(newDir, "php.ini");
        if (File.Exists(oldIni))
        {
            progress?.Report("Carrying over php.ini...");
            File.Copy(oldIni, newIni, overwrite: true);
            PhpService.PatchExtensionDir(newIni, newDir, backup: false);
        }

        var cacert = Path.Combine(oldDir, "cacert.pem");
        if (File.Exists(cacert) && !File.Exists(Path.Combine(newDir, "cacert.pem")))
        {
            File.Copy(cacert, Path.Combine(newDir, "cacert.pem"));
            if (File.Exists(newIni))
                PhpIniBackup.Write(newIni, PhpIniEditor.SetDirective(File.ReadAllText(newIni), "openssl.cafile", $"\"{Path.Combine(newDir, "cacert.pem")}\""));
        }

        if (wasActive)
            PhpService.SetActiveVersion(newVersion);

        return $"Upgraded {oldVersion} to {newVersion}: php.ini and {copied.Count} extra extension file(s) carried over" +
               (wasActive ? "; the new version is active." : ".") + $" {oldVersion} is kept for rollback.";
    }

    // --- 8. Settings bundle ----------------------------------------------------------------------

    public static SettingsBundle CreateBundle()
    {
        var settings = PhpService.LoadSettings();
        var bundle = new SettingsBundle
        {
            ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            BasePath = settings.BasePath,
            FrankenPhpPath = settings.FrankenPhpPath,
            UrlsJson = UrlDatabase.ExportJson()
        };

        var active = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(active) || !File.Exists(PhpService.IniPathFor(active)))
            return bundle;

        bundle.PhpVersion = active;
        foreach (var (key, value) in PhpService.ReadSettings(active))
            if (value != null)
                bundle.Directives[key] = value;

        var extensions = PhpService.GetExtensions(active).Where(e => !e.IsBuiltIn).ToList();
        bundle.EnabledExtensions = extensions.Where(e => e.Enabled && e.Kind == ExtensionKind.Dll).Select(e => e.Name).ToList();
        bundle.DisabledExtensions = extensions.Where(e => !e.Enabled && e.Kind == ExtensionKind.Dll).Select(e => e.Name).ToList();
        bundle.OpcacheLoaded = PhpService.IsOpcacheLoaded(active);
        return bundle;
    }

    /// <summary>Applies a bundle: paths and URLs always; php.ini directives and extensions to the active PHP when there is one.</summary>
    public static string ApplyBundle(SettingsBundle bundle)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(bundle.BasePath))
        {
            PhpService.SaveSettings(new PhpService.AppSettings { BasePath = bundle.BasePath, FrankenPhpPath = bundle.FrankenPhpPath });
            parts.Add("paths");
        }

        if (!string.IsNullOrWhiteSpace(bundle.UrlsJson))
        {
            var count = UrlDatabase.ImportJson(bundle.UrlsJson, overwriteExisting: true);
            parts.Add($"{count} URL(s)");
        }

        var active = PhpService.GetActiveVersion();
        if (!string.IsNullOrEmpty(active) && File.Exists(PhpService.IniPathFor(active)))
        {
            var ini = PhpService.IniPathFor(active);
            var content = File.ReadAllText(ini);
            var directives = 0;

            foreach (var (key, value) in bundle.Directives)
            {
                var def = IniSettingDefs.Find(key);
                if (def != null && IniSettingDefs.Validate(def, value) != null)
                    continue;
                content = PhpIniEditor.SetDirective(content, key, value);
                directives++;
            }

            var extDir = Path.Combine(InstallDir(active), "ext");
            var enable = bundle.EnabledExtensions.Where(n => File.Exists(Path.Combine(extDir, $"php_{n}.dll"))).ToList();
            content = LaravelExtensions.Enable(content, enable);
            content = PhpIniEditor.DisableExtensions(content, bundle.DisabledExtensions);
            if (File.Exists(Path.Combine(extDir, "php_opcache.dll")))
                content = PhpIniEditor.SetExtensionState(content, "opcache", bundle.OpcacheLoaded, zend: true);

            PhpIniBackup.Write(ini, content);
            var skipped = bundle.EnabledExtensions.Count - enable.Count;
            parts.Add($"{directives} php.ini setting(s), {enable.Count} extension(s) enabled" +
                      (skipped > 0 ? $" ({skipped} skipped: DLL not installed here)" : ""));
        }
        else
        {
            parts.Add("no active PHP, so php.ini settings were not applied");
        }

        return "Imported " + string.Join("; ", parts) + ".";
    }

    // --- 7. FrankenPHP helpers -------------------------------------------------------------------

    /// <summary>Names of FrankenPHP services that are currently running.</summary>
    public static List<string> RunningFrankenServices()
    {
        FrankenPhpDatabase.Initialize();
        return FrankenPhpDatabase.GetAll()
            .Where(s => PhpService.IsServiceRunning(s.ServiceName))
            .Select(s => s.ServiceName)
            .ToList();
    }

    /// <summary>The newest lines of a service's stdout and stderr logs, interleaved by file.</summary>
    public static string ReadServiceLog(string serviceName, int maxLines = 200)
    {
        var logsDir = Path.Combine(PhpService.FrankenPhpPath, "services", serviceName, "logs");
        var sb = new StringBuilder();
        foreach (var name in new[] { "stderr.log", "stdout.log" })
        {
            var file = Path.Combine(logsDir, name);
            if (!File.Exists(file))
                continue;

            sb.AppendLine($"== {name} ==");
            foreach (var line in TailLines(file, maxLines))
                sb.AppendLine(line);
            sb.AppendLine();
        }

        return sb.Length == 0 ? $"No log files yet in {logsDir}." : sb.ToString();
    }

    /// <summary>Last N lines of a file that may be open for writing by the service.</summary>
    public static List<string> TailLines(string file, int count)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // read at most the last 256 KB, plenty for 200 lines
        const int window = 256 * 1024;
        if (stream.Length > window)
            stream.Seek(-window, SeekOrigin.End);

        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n').ToList();
        if (stream.Length > window && lines.Count > 0)
            lines.RemoveAt(0); // first line is probably cut in half
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return lines.Count <= count ? lines : lines.GetRange(lines.Count - count, count);
    }
}
