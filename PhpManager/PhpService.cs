using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhpManager;

public class PhpVersionInfo
{
    public string Version { get; set; } = "";
    public string InstallDir { get; set; } = "";
    public bool Installed { get; set; }
    public bool Active { get; set; }
    public bool IsFrankenPhp { get; set; }
    public string Display => Active ? $"{Version}  (active)" : Installed ? Version : $"{Version}  (incomplete)";
}

public class ExtensionInfo
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string Status => Enabled ? "ON" : "OFF";
}

public static class PhpService
{
    public static string BasePath { get; set; } = "C:\\PHP";

    private static readonly string[] DownloadUrls = Urls.PhpDownload.Mirrors;

    private static readonly string[] CatalogUrls = Urls.PhpCatalog.Mirrors;

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    { Timeout = TimeSpan.FromMinutes(5) };

    // --- Cache Helpers ---

    private static string? ResolveCacheFile(string fileName)
    {
        var primary = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(primary))
            return primary;

        var fallback = Path.Combine(Path.GetTempPath(), "php-manager-scripts", fileName);
        if (File.Exists(fallback))
            return fallback;

        return null;
    }

    private static void WriteCacheFile(string fileName, string content)
    {
        var primaryDir = Path.GetDirectoryName(Path.Combine(AppContext.BaseDirectory, fileName))!;
        var fallbackDir = Path.Combine(Path.GetTempPath(), "php-manager-scripts");
        Directory.CreateDirectory(primaryDir);
        Directory.CreateDirectory(fallbackDir);
        File.WriteAllText(Path.Combine(primaryDir, fileName), content, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(fallbackDir, fileName), content, new UTF8Encoding(false));
    }

    // --- Version Management ---

    public static string GetActiveVersion()
    {
        var versionFile = Path.Combine(BasePath, ".php-active-version");
        if (!File.Exists(versionFile))
            return "";

        var version = File.ReadAllText(versionFile).Trim();

        if (string.IsNullOrEmpty(version))
            return "";

        if (version == "frankenphp")
            return IsFrankenPhpInstalled() ? "frankenphp" : "";

        if (Directory.Exists(Path.Combine(BasePath, version)))
            return version;

        return "";
    }

    public static bool IsFrankenPhpActive() => GetActiveVersion() == "frankenphp";

    public static void SetActiveVersion(string version, bool addToSystemPath = false)
    {
        if (version == "frankenphp")
        {
            if (!IsFrankenPhpInstalled())
                throw new FileNotFoundException("FrankenPHP not found. Install FrankenPHP first.");

            File.WriteAllText(Path.Combine(BasePath, ".php-active-version"), "frankenphp", new UTF8Encoding(false));

            var frankenPattern = $@"^{Regex.Escape(BasePath)}\\frankenphp\\?$";

            // User PATH
            var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            var userEntries = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(e => !Regex.IsMatch(e.Trim(), frankenPattern))
                .Prepend(FrankenPhpPath)
                .ToList();
            Environment.SetEnvironmentVariable("Path", string.Join(";", userEntries), EnvironmentVariableTarget.User);

            // Process PATH
            var processEntries = (Environment.GetEnvironmentVariable("Path") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(e => !Regex.IsMatch(e.Trim(), frankenPattern))
                .Prepend(FrankenPhpPath)
                .ToList();
            Environment.SetEnvironmentVariable("Path", string.Join(";", processEntries));

            // PHPRC + FRANKENPHP_EXT_DIR
            var frankenIni = Path.Combine(FrankenPhpPath, "php.ini");
            if (File.Exists(frankenIni))
                Environment.SetEnvironmentVariable("PHPRC", frankenIni);
            Environment.SetEnvironmentVariable("FRANKENPHP_EXT_DIR", FrankenPhpPath);

            if (addToSystemPath && IsRunningAsAdmin())
                UpdateFrankenPhpSystemPath();

            return;
        }

        var versionDir = Path.Combine(BasePath, version);
        if (!Directory.Exists(versionDir))
            throw new DirectoryNotFoundException($"PHP {version} not found at {versionDir}");

        var phpExe = Path.Combine(versionDir, "php.exe");
        if (!File.Exists(phpExe))
            throw new FileNotFoundException($"php.exe not found in {versionDir}");

        File.WriteAllText(Path.Combine(BasePath, ".php-active-version"), version, new UTF8Encoding(false));

        var versionPattern = $@"^{Regex.Escape(BasePath)}\\[\d\.]+\\?$";

        // User PATH
        var regUserPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var regUserEntries = regUserPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !Regex.IsMatch(e.Trim(), versionPattern))
            .Prepend(versionDir)
            .ToList();
        Environment.SetEnvironmentVariable("Path", string.Join(";", regUserEntries), EnvironmentVariableTarget.User);

        // Process PATH
        var regProcessEntries = (Environment.GetEnvironmentVariable("Path") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !Regex.IsMatch(e.Trim(), versionPattern))
            .Prepend(versionDir)
            .ToList();
        Environment.SetEnvironmentVariable("Path", string.Join(";", regProcessEntries));

        // PHPRC
        Environment.SetEnvironmentVariable("PHPRC", Path.Combine(versionDir, "php.ini"));

        // Clear FRANKENPHP_EXT_DIR (regular PHP uses ext/ subdir, not FRANKENPHP_EXT_DIR)
        Environment.SetEnvironmentVariable("FRANKENPHP_EXT_DIR", "");

        // System PATH (requires admin)
        if (addToSystemPath && IsRunningAsAdmin())
        {
            var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            var machineEntries = machinePath.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(e => !Regex.IsMatch(e.Trim(), versionPattern))
                .Prepend(versionDir)
                .ToList();
            Environment.SetEnvironmentVariable("Path", string.Join(";", machineEntries), EnvironmentVariableTarget.Machine);
        }
    }

    public static List<PhpVersionInfo> GetInstalledVersions()
    {
        if (!Directory.Exists(BasePath))
            return [];

        var activeVersion = GetActiveVersion();
        var versions = new List<PhpVersionInfo>();

        if (IsFrankenPhpInstalled())
        {
            versions.Add(new PhpVersionInfo
            {
                Version = "FrankenPHP",
                InstallDir = FrankenPhpPath,
                Installed = true,
                Active = activeVersion == "frankenphp",
                IsFrankenPhp = true
            });
        }

        versions.AddRange(Directory.GetDirectories(BasePath)
            .Select(dir => Path.GetFileName(dir))
            .Where(name => Regex.IsMatch(name, @"^\d+\.\d+\.\d+$"))
            .Select(name => new PhpVersionInfo
            {
                Version = name,
                InstallDir = Path.Combine(BasePath, name),
                Installed = File.Exists(Path.Combine(BasePath, name, "php.exe")),
                Active = name == activeVersion
            }));

        return versions.OrderByDescending(v => v.IsFrankenPhp).ThenByDescending(v => v.Version).ToList();
    }

    public static List<string> GetAvailableVersions()
    {
        var path = ResolveCacheFile(".php-versions.cache");
        if (path == null)
            return [];

        try
        {
            var versions = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            return versions?.OrderByDescending(v => Version.Parse(v)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    // --- Install / Download ---

    public static async Task<string> InstallVersionAsync(string version, IProgress<string>? progress = null)
    {
        var installDir = Path.Combine(BasePath, version);
        var phpExe = Path.Combine(installDir, "php.exe");

        if (File.Exists(phpExe))
            return $"PHP {version} already installed at {installDir}";

        Directory.CreateDirectory(installDir);
        await DownloadAndExtractAsync(version, installDir, progress);

        if (!File.Exists(phpExe))
            throw new FileNotFoundException($"php.exe not found at {phpExe} after extraction");

        return $"PHP {version} installed at {installDir}";
    }

    public static async Task RemoveVersionAsync(string version)
    {
        if (GetActiveVersion() == version)
            throw new InvalidOperationException("Cannot remove the active PHP version. Switch to another version first.");

        var versionDir = Path.Combine(BasePath, version);
        if (!Directory.Exists(versionDir))
            throw new DirectoryNotFoundException($"PHP {version} not found at {versionDir}");

        await Task.Run(() => Directory.Delete(versionDir, recursive: true));
    }

    private static async Task DownloadAndExtractAsync(string version, string targetDir, IProgress<string>? progress)
    {
        var archive = Path.Combine(Path.GetTempPath(), $"php-{version}-nts-x64.zip");

        foreach (var urlTemplate in DownloadUrls)
        {
            var url = string.Format(urlTemplate, version);
            try
            {
                progress?.Report($"Downloading from {url}...");
                LogWindow.LogDownload(url);
                var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fs = File.Create(archive);
                await stream.CopyToAsync(fs);
                progress?.Report("Download complete.");
                LogWindow.LogSuccess($"PHP {version} download complete.");
                break;
            }
            catch (Exception ex)
            {
                progress?.Report($"Failed: {ex.Message}");
                LogWindow.LogWarn($"Download failed from {url}: {ex.Message}");
            }
        }

        if (!File.Exists(archive))
            throw new Exception($"Unable to download PHP {version} from any configured URL.");

        progress?.Report("Extracting...");
        LogWindow.LogExtract(targetDir);
        ZipFile.ExtractToDirectory(archive, targetDir, overwriteFiles: true);
        File.Delete(archive);
        progress?.Report("Extraction complete.");
        LogWindow.LogSuccess($"PHP {version} extracted to {targetDir}");
    }

    // --- Catalog ---

    public static async Task<int> RefreshCatalogAsync(IProgress<string>? progress = null)
    {
        var allVersions = new HashSet<string>();

        foreach (var indexUrl in CatalogUrls)
        {
            try
            {
                progress?.Report($"Reading {indexUrl}...");
                var html = await Http.GetStringAsync(indexUrl);
                foreach (Match m in Regex.Matches(html, Urls.PhpCatalog.RegexPattern, RegexOptions.IgnoreCase))
                    allVersions.Add(m.Groups[1].Value);
            }
            catch (Exception ex)
            {
                progress?.Report($"Failed to read {indexUrl}: {ex.Message}");
            }
        }

        if (allVersions.Count == 0)
            throw new Exception("No compatible PHP versions found in any catalog.");

        var sorted = allVersions.OrderByDescending(v => Version.Parse(v)).ToList();
        WriteCacheFile(".php-versions.cache", JsonSerializer.Serialize(sorted));

        progress?.Report($"Cached {sorted.Count} PHP versions.");
        return sorted.Count;
    }

    // --- php.ini Management ---

    public static void ConfigurePhpIni(string version, string source)
    {
        var phpIni = Path.Combine(BasePath, version, "php.ini");

        if (!File.Exists(source))
            throw new FileNotFoundException($"Source php.ini not found: {source}");

        File.Copy(source, phpIni, overwrite: true);
        PatchExtensionDir(phpIni, Path.Combine(BasePath, version));
    }

    public static void PatchExtensionDir(string phpIniPath, string installDir)
    {
        if (!File.Exists(phpIniPath))
            return;

        var content = File.ReadAllText(phpIniPath);
        var extDir = Path.Combine(installDir, "ext");
        content = Regex.Replace(content, @"(?m)^\s*;?\s*extension_dir\s*=.*$",
            $"extension_dir = \"{extDir}\"");
        File.WriteAllText(phpIniPath, content, new UTF8Encoding(false));
    }

    public static List<ExtensionInfo> GetExtensions(string? version = null)
    {
        version ??= GetActiveVersion();
        if (string.IsNullOrEmpty(version))
            return [];

        var phpIni = Path.Combine(BasePath, version, "php.ini");
        if (!File.Exists(phpIni))
            return [];

        var content = File.ReadAllText(phpIni);
        return Regex.Matches(content, @"(?m)^\s*;?\s*extension\s*=\s*(?:php_)?(\w+)(?:\.dll)?\s*$")
            .Select(m => new ExtensionInfo
            {
                Name = m.Groups[1].Value,
                Enabled = !m.Value.TrimStart().StartsWith(";")
            })
            .ToList();
    }

    public static void SaveExtensions(string version, List<ExtensionInfo> extensions)
    {
        var phpIni = Path.Combine(BasePath, version, "php.ini");
        if (!File.Exists(phpIni))
            return;

        var content = File.ReadAllText(phpIni);

        foreach (var ext in extensions)
        {
            var pattern = $@"(?m)^\s*;?\s*extension\s*=\s*(?:php_)?{Regex.Escape(ext.Name)}(?:\.dll)?\s*$";
            var replacement = ext.Enabled ? $"extension={ext.Name}" : $";extension={ext.Name}";

            if (Regex.IsMatch(content, pattern))
            {
                content = Regex.Replace(content, pattern, replacement);
            }
            else if (ext.Enabled)
            {
                var nextSection = Regex.Match(content, @"(?m)^\[(?!PHP\])");
                var directive = $"extension={ext.Name}\n";
                content = nextSection.Success
                    ? content.Insert(nextSection.Index, directive)
                    : content.TrimEnd() + "\n" + directive;
            }
        }

        File.WriteAllText(phpIni, content, new UTF8Encoding(false));
    }

    // --- CA Certificate ---

    public static async Task InstallCacertAsync(string? version = null, IProgress<string>? progress = null)
    {
        version ??= GetActiveVersion();
        if (string.IsNullOrEmpty(version))
            throw new InvalidOperationException("No active PHP version.");

        var installDir = Path.Combine(BasePath, version);
        var phpIni = Path.Combine(installDir, "php.ini");
        var cacertDest = Path.Combine(installDir, "cacert.pem");

        if (!File.Exists(cacertDest))
        {
            progress?.Report("Downloading cacert.pem...");
            LogWindow.LogDownload(Urls.Cacert.DownloadUrl);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var response = await Http.GetAsync(Urls.Cacert.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var fs = File.Create(cacertDest);
            await stream.CopyToAsync(fs, cts.Token);

            LogWindow.LogSuccess("cacert.pem downloaded.");
        }

        if (!File.Exists(phpIni))
            throw new FileNotFoundException($"php.ini not found at {phpIni}");

        var content = File.ReadAllText(phpIni);
        var cacertPath = cacertDest.Replace("\\", "/");

        content = PatchIniValue(content, "curl.cainfo", $"curl.cainfo = \"{cacertPath}\"");
        content = PatchIniValue(content, "openssl.cafile", $"openssl.cafile = \"{cacertPath}\"");

        File.WriteAllText(phpIni, content, new UTF8Encoding(false));
        progress?.Report("CA certificate configured.");
        LogWindow.LogSuccess("CA certificate configured in php.ini.");
    }

    // --- PHP Execution ---

    public static List<string> GetLoadedModules(string? version = null)
    {
        version ??= GetActiveVersion();
        if (string.IsNullOrEmpty(version))
            return [];

        var phpExe = Path.Combine(BasePath, version, "php.exe");
        var phpIni = Path.Combine(BasePath, version, "php.ini");

        if (!File.Exists(phpExe))
            return [];

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = phpExe,
                Arguments = "-m",
                WorkingDirectory = Path.GetDirectoryName(phpExe),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment = { ["PHPRC"] = phpIni }
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && char.IsLetter(l[0]))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // --- Extension Verification ---

    public static (List<string> Loaded, List<string> Failed, List<string> NoDll) VerifyExtensions(string? version = null)
    {
        version ??= GetActiveVersion();
        if (string.IsNullOrEmpty(version))
            return ([], [], []);

        var installDir = Path.Combine(BasePath, version);
        var phpIni = Path.Combine(installDir, "php.ini");
        var extDir = Path.Combine(installDir, "ext");
        var phpExe = Path.Combine(installDir, "php.exe");

        if (!File.Exists(phpExe) || !File.Exists(phpIni))
            return ([], [], []);

        // Get loaded modules from php -m
        var loadedModules = GetLoadedModules(version);

        // Get available DLLs
        var availableDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(extDir))
        {
            foreach (var dll in Directory.GetFiles(extDir, "*.dll"))
                availableDlls.Add(Path.GetFileNameWithoutExtension(dll).Replace("php_", ""));
        }

        // Get extensions enabled in php.ini
        var iniContent = File.ReadAllText(phpIni);
        var enabledExtensions = Regex.Matches(iniContent, @"(?m)^\s*extension\s*=\s*(?:php_)?(\w+)(?:\.dll)?\s*$")
            .Select(m => m.Groups[1].Value)
            .ToList();

        var loaded = new List<string>();
        var failed = new List<string>();
        var noDll = new List<string>();

        foreach (var ext in enabledExtensions)
        {
            if (loadedModules.Contains(ext, StringComparer.OrdinalIgnoreCase))
                loaded.Add(ext);
            else if (availableDlls.Contains(ext))
                failed.Add(ext);
            else
                noDll.Add(ext);
        }

        return (loaded, failed, noDll);
    }

    public static string GetPhpVersionString(string? version = null)
    {
        version ??= GetActiveVersion();
        if (string.IsNullOrEmpty(version))
            return "";

        var phpExe = Path.Combine(BasePath, version, "php.exe");
        if (!File.Exists(phpExe))
            return "";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = phpExe,
                Arguments = "--version",
                WorkingDirectory = Path.GetDirectoryName(phpExe),
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadLine() ?? "";
            process.WaitForExit();
            return output.Trim();
        }
        catch
        {
            return "";
        }
    }

    // --- PHP Build Info (for extension downloads) ---

    private static (string MajorMinor, string ThreadSafety, string Architecture, string Compiler) GetPhpBuildInfo(string version)
    {
        var phpExe = Path.Combine(BasePath, version, "php.exe");
        if (!File.Exists(phpExe))
            throw new FileNotFoundException($"php.exe not found at {phpExe}");

        var (output, _, exitCode) = RunPhp("--version", Path.Combine(BasePath, version));
        if (exitCode != 0)
            throw new Exception("Unable to determine PHP version.");

        var versionMatch = Regex.Match(output, @"^PHP (\d+)\.(\d+)\.");
        if (!versionMatch.Success)
            throw new Exception("Unable to parse PHP version.");

        var major = versionMatch.Groups[1].Value;
        var minor = versionMatch.Groups[2].Value;
        var majorMinor = $"{major}{minor}";

        var (phpInfo, _, phpInfoExit) = RunPhp("-i", Path.Combine(BasePath, version));
        if (phpInfoExit != 0)
            throw new Exception("Unable to run php -i.");

        var threadSafety = phpInfo.Contains("Thread Safety => enabled") ? "ts" : "nts";
        var architecture = phpInfo.Contains("Architecture => x64") ? "x64" : "x86";

        var compilerMatch = Regex.Match(phpInfo, @"PHP Extension Build => .*,(VS\d+)");
        var compiler = compilerMatch.Success
            ? compilerMatch.Groups[1].Value.ToLowerInvariant()
            : (int.Parse(major) * 10 + int.Parse(minor)) >= 84 ? "vs17" : "vs16";

        return (majorMinor, threadSafety, architecture, compiler);
    }

    // --- Redis Extension ---

    public static async Task InstallRedisAsync(string version, string extensionVersion = "6.3.0", IProgress<string>? progress = null)
    {
        var installDir = Path.Combine(BasePath, version);
        var phpExe = Path.Combine(installDir, "php.exe");
        var phpIni = Path.Combine(installDir, "php.ini");
        var extDir = Path.Combine(installDir, "ext");

        if (!File.Exists(phpExe))
            throw new FileNotFoundException($"php.exe not found at {phpExe}");
        if (!Directory.Exists(extDir))
            throw new DirectoryNotFoundException($"Extension directory not found at {extDir}");

        var (majorMinor, threadSafety, architecture, compiler) = GetPhpBuildInfo(version);
        var package = $"php_redis-{extensionVersion}-{majorMinor}-{threadSafety}-{compiler}-{architecture}";
        var downloadUrl = Urls.Redis.DownloadUrl(extensionVersion, majorMinor, threadSafety, compiler, architecture);

        await DownloadExtractEnablePackage(downloadUrl, package, installDir, extDir, phpIni,
            "redis", ["redis"], progress, $"Redis extension {extensionVersion}");
    }

    // --- SQL Server Drivers ---

    public static async Task InstallSqlServerAsync(string version, string driverVersion = "5.13.3", IProgress<string>? progress = null)
    {
        var installDir = Path.Combine(BasePath, version);
        var phpExe = Path.Combine(installDir, "php.exe");
        var phpIni = Path.Combine(installDir, "php.ini");
        var extDir = Path.Combine(installDir, "ext");

        if (!File.Exists(phpExe))
            throw new FileNotFoundException($"php.exe not found at {phpExe}");
        if (!Directory.Exists(extDir))
            throw new DirectoryNotFoundException($"Extension directory not found at {extDir}");

        var (majorMinor, threadSafety, architecture, _) = GetPhpBuildInfo(version);
        var downloadUrl = Urls.SqlServer.DownloadUrl(driverVersion);
        var archive = Path.Combine(Path.GetTempPath(), $"msphpsql-{driverVersion}.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), $"msphpsql-{driverVersion}");

        try
        {
            progress?.Report($"Downloading SQL Server drivers from {downloadUrl}...");
            var response = await Http.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();
            await using (var fs = File.Create(archive))
                await response.Content.CopyToAsync(fs);

            progress?.Report("Extracting...");
            ZipFile.ExtractToDirectory(archive, extractPath, overwriteFiles: true);

            var driverDir = Path.Combine(extractPath, "Windows");
            var pdoDll = Path.Combine(driverDir, $"php_pdo_sqlsrv_{majorMinor}_{threadSafety}_{architecture}.dll");
            var sqlDll = Path.Combine(driverDir, $"php_sqlsrv_{majorMinor}_{threadSafety}_{architecture}.dll");

            if (!File.Exists(pdoDll) || !File.Exists(sqlDll))
                throw new FileNotFoundException(
                    $"SQL Server drivers unavailable for PHP {majorMinor} {threadSafety} {architecture} in release {driverVersion}.");

            File.Copy(pdoDll, Path.Combine(extDir, "php_pdo_sqlsrv.dll"), overwrite: true);
            File.Copy(sqlDll, Path.Combine(extDir, "php_sqlsrv.dll"), overwrite: true);

            progress?.Report("Enabling in php.ini...");
            EnableExtension(phpIni, "pdo_sqlsrv");
            EnableExtension(phpIni, "sqlsrv");

            progress?.Report($"SQL Server drivers {driverVersion} installed successfully.");
        }
        finally
        {
            CleanupTemp(archive, extractPath);
        }
    }

    // --- Settings ---

    private static readonly string SettingsFile = Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static string FrankenPhpPath { get; set; } = "";

    public class AppSettings
    {
        public string BasePath { get; set; } = "C:\\PHP";
        public string FrankenPhpPath { get; set; } = "";
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        BasePath = settings.BasePath;
        FrankenPhpPath = settings.FrankenPhpPath;
    }

    static PhpService()
    {
        var settings = LoadSettings();
        if (!string.IsNullOrEmpty(settings.BasePath))
            BasePath = settings.BasePath;
        if (!string.IsNullOrEmpty(settings.FrankenPhpPath))
            FrankenPhpPath = settings.FrankenPhpPath;

        if (string.IsNullOrEmpty(FrankenPhpPath))
            FrankenPhpPath = Path.Combine(BasePath, "frankenphp");

        FrankenPhpDatabase.Initialize();
    }

    // --- FrankenPHP ---

    public static string FrankenPhpExe => Path.Combine(FrankenPhpPath, "frankenphp.exe");

    public static bool IsFrankenPhpInstalled() => File.Exists(FrankenPhpExe);

    public static string GetFrankenPhpVersion()
    {
        if (!File.Exists(FrankenPhpExe))
            return "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FrankenPhpExe,
                Arguments = "version",
                WorkingDirectory = FrankenPhpPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadLine() ?? "";
            process.WaitForExit();
            return output.Trim();
        }
        catch { return ""; }
    }

    public static async Task InstallFrankenPhpAsync(string version = "latest", IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(FrankenPhpPath);

        var url = Urls.FrankenPhp.DownloadUrl(version);
        var archive = Path.Combine(Path.GetTempPath(), "frankenphp.zip");

        try
        {
            progress?.Report($"Downloading FrankenPHP from {url}...");
            LogWindow.LogDownload(url);
            var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var stream = await response.Content.ReadAsStreamAsync())
            await using (var fs = File.Create(archive))
                await stream.CopyToAsync(fs);

            progress?.Report("Extracting...");
            LogWindow.LogExtract(FrankenPhpPath);
            ZipFile.ExtractToDirectory(archive, FrankenPhpPath, overwriteFiles: true);
            progress?.Report("FrankenPHP installed.");
            LogWindow.LogSuccess("FrankenPHP runtime installed.");
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
        }
    }

    public static async Task<FrankenPhpServiceRecord> InstallFrankenPhpServiceAsync(FrankenPhpServiceRecord record, IProgress<string>? progress = null)
    {
        if (!File.Exists(FrankenPhpExe))
            throw new FileNotFoundException("frankenphp.exe not found. Install FrankenPHP first.");

        if (record.Port == record.AdminPort)
            throw new ArgumentException("Public and admin ports must be different.");

        FrankenPhpDatabase.Initialize();
        if (FrankenPhpDatabase.ExistsByServiceName(record.ServiceName, record.Id > 0 ? record.Id : null))
            throw new ArgumentException($"Service '{record.ServiceName}' already exists.");
        if (FrankenPhpDatabase.PortInUse(record.Port, record.Id > 0 ? record.Id : null))
            throw new ArgumentException($"Port {record.Port} is already in use by another service.");
        if (FrankenPhpDatabase.PortInUse(record.AdminPort, record.Id > 0 ? record.Id : null))
            throw new ArgumentException($"Admin port {record.AdminPort} is already in use by another service.");

        var serviceDir = Path.Combine(FrankenPhpPath, "services", record.ServiceName);
        Directory.CreateDirectory(serviceDir);
        Directory.CreateDirectory(Path.Combine(serviceDir, "logs"));

        var caddyfile = Path.Combine(serviceDir, "Caddyfile");
        var publicPath = Path.Combine(record.AppPath, "public");
        var appEnv = record.IsDevelopment ? "local" : "production";
        var appDebug = record.IsDevelopment ? "true" : "false";

        var caddyContent = $@"{{
    admin 127.0.0.1:{record.AdminPort}
    auto_https off

    frankenphp {{
        worker {{
            file ""{publicPath.Replace("\\", "/")}/frankenphp-worker.php""
            num {record.Workers}
        }}
    }}
}}

:{record.Port} {{
    root * ""{publicPath.Replace("\\", "/")}""
    encode zstd br gzip

    log {{
        level WARN
        format json
    }}

    php_server {{
        index frankenphp-worker.php
        try_files {{path}} frankenphp-worker.php
        resolve_root_symlink
    }}
}}
";
        File.WriteAllText(caddyfile, caddyContent, new UTF8Encoding(false));
        progress?.Report("Caddyfile written.");

        var phpVersion = GetActiveVersion();
        var phpDir = string.IsNullOrEmpty(phpVersion) ? "" : Path.Combine(BasePath, phpVersion);
        var extDir = string.IsNullOrEmpty(phpDir) ? "" : Path.Combine(phpDir, "ext");
        var phpIni = string.IsNullOrEmpty(phpDir) ? "" : Path.Combine(phpDir, "php.ini");

        var envVars = new Dictionary<string, string>
        {
            ["APP_ENV"] = appEnv,
            ["APP_DEBUG"] = appDebug,
            ["APP_BASE_PATH"] = record.AppPath,
            ["APP_PUBLIC_PATH"] = publicPath,
            ["LARAVEL_OCTANE"] = "1",
            ["MAX_REQUESTS"] = record.MaxRequests.ToString(),
            ["REQUEST_MAX_EXECUTION_TIME"] = "30",
            ["OCTANE_PORT"] = record.Port.ToString(),
            ["OCTANE_ADMIN_PORT"] = record.AdminPort.ToString(),
            ["OCTANE_WORKERS"] = record.Workers.ToString()
        };
        if (!string.IsNullOrEmpty(phpIni)) envVars["PHPRC"] = phpIni;
        if (!string.IsNullOrEmpty(extDir)) envVars["FRANKENPHP_EXT_DIR"] = extDir;

        var envStr = string.Join(";", envVars.Select(kv => $"{kv.Key}={kv.Value}"));
        var processParams = $"run --config \"{caddyfile}\"";

        var displayName = string.IsNullOrEmpty(record.DisplayName) ? $"FrankenPHP - {record.ServiceName}" : record.DisplayName;
        var description = string.IsNullOrEmpty(record.Description) ? "FrankenPHP and Laravel Octane service." : record.Description;

        var servyArgs = $"install --name=\"{record.ServiceName}\" " +
            $"--displayName=\"{displayName}\" " +
            $"--description=\"{description}\" " +
            $"--path=\"{FrankenPhpExe}\" " +
            $"--startupDir=\"{record.AppPath}\" " +
            "--startupType=Automatic --priority=Normal " +
            "--startTimeout=30 --stopTimeout=30 " +
            $"--stdout=\"{Path.Combine(serviceDir, "logs", "stdout.log")}\" " +
            $"--stderr=\"{Path.Combine(serviceDir, "logs", "stderr.log")}\" " +
            "--enableSizeRotation --rotationSize=10 --maxRotations=8 " +
            "--enableHealth --heartbeatInterval=10 --maxFailedChecks=3 " +
            "--recoveryAction=RestartProcess --recoveryOnCleanExit " +
            "--maxRestartAttempts=5 " +
            $"--environment=\"{envStr}\" " +
            $"--processParameters=\"{processParams}\" " +
            "--quiet";

        var psi = new ProcessStartInfo
        {
            FileName = "servy-cli",
            Arguments = servyArgs,
            WorkingDirectory = FrankenPhpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new Exception($"servy-cli install failed: {error}");

        if (record.Id > 0)
            FrankenPhpDatabase.Update(record);
        else
            record = FrankenPhpDatabase.Insert(record);

        progress?.Report($"Service '{record.ServiceName}' installed.");
        LogWindow.LogService(record.ServiceName, "installed via servy-cli");
        return record;
    }

    public static async Task RemoveFrankenPhpServiceAsync(FrankenPhpServiceRecord record, IProgress<string>? progress)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "servy-cli",
            Arguments = $"uninstall --name=\"{record.ServiceName}\" --quiet",
            WorkingDirectory = FrankenPhpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();

        FrankenPhpDatabase.Delete(record.Id);
        progress?.Report($"Service '{record.ServiceName}' removed.");
        LogWindow.LogService(record.ServiceName, "removed");
    }

    public static async Task StartFrankenPhpServiceAsync(FrankenPhpServiceRecord record, IProgress<string>? progress)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "servy-cli",
            Arguments = $"start --name=\"{record.ServiceName}\" --quiet",
            WorkingDirectory = FrankenPhpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
        progress?.Report($"Service '{record.ServiceName}' started.");
        LogWindow.LogService(record.ServiceName, "started");
    }

    public static async Task StopFrankenPhpServiceAsync(FrankenPhpServiceRecord record, IProgress<string>? progress)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "servy-cli",
            Arguments = $"stop --name=\"{record.ServiceName}\" --quiet",
            WorkingDirectory = FrankenPhpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
        progress?.Report($"Service '{record.ServiceName}' stopped.");
        LogWindow.LogService(record.ServiceName, "stopped");
    }

    public static bool IsServiceRunning(string serviceName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "servy-cli",
                Arguments = $"status --name=\"{serviceName}\"",
                WorkingDirectory = FrankenPhpPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Contains("running", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool IsServyInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "servy-cli",
                Arguments = "version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    public static async Task InstallServyAsync(IProgress<string>? progress = null)
    {
        var servyDir = Path.Combine(FrankenPhpPath, "servy");
        Directory.CreateDirectory(servyDir);
        var servyExe = Path.Combine(servyDir, "servy-cli.exe");
        var url = Urls.Servy.DownloadUrl;
        var archive = Path.Combine(Path.GetTempPath(), "servy-cli.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), "servy-cli-extract");

        try
        {
            progress?.Report("Downloading servy-cli...");
            LogWindow.LogDownload(url);
            var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var stream = await response.Content.ReadAsStreamAsync())
            await using (var fs = File.Create(archive))
                await stream.CopyToAsync(fs);

            progress?.Report("Extracting servy-cli...");
            LogWindow.LogExtract(servyDir);
            ZipFile.ExtractToDirectory(archive, extractPath, overwriteFiles: true);

            var exe = Directory.GetFiles(extractPath, "servy-cli.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe == null)
                throw new FileNotFoundException("servy-cli.exe not found in downloaded package.");

            File.Copy(exe, servyExe, overwrite: true);

            // Add to user PATH
            var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            if (!userPath.Contains(servyDir, StringComparison.OrdinalIgnoreCase))
            {
                var entries = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries).Prepend(servyDir).ToList();
                Environment.SetEnvironmentVariable("Path", string.Join(";", entries), EnvironmentVariableTarget.User);
            }

            progress?.Report("servy-cli installed successfully.");
            LogWindow.LogSuccess("servy-cli installed.");
        }
        finally
        {
            CleanupTemp(archive, extractPath);
        }
    }

    public static void UpdateFrankenPhpSystemPath()
    {
        if (!Directory.Exists(FrankenPhpPath))
            throw new DirectoryNotFoundException($"FrankenPHP not found at {FrankenPhpPath}");

        var escapedBase = Regex.Escape(BasePath);
        var pattern = $@"^{escapedBase}\\frankenphp\\?$";

        var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var entries = machinePath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !Regex.IsMatch(e.Trim(), pattern))
            .Prepend(FrankenPhpPath)
            .ToList();
        Environment.SetEnvironmentVariable("Path", string.Join(";", entries), EnvironmentVariableTarget.Machine);

        Environment.SetEnvironmentVariable("FRANKENPHP_EXT_DIR", FrankenPhpPath, EnvironmentVariableTarget.Machine);
    }

    public static (string Output, string Error, int ExitCode) RunFrankenPhp(string args)
    {
        if (!File.Exists(FrankenPhpExe))
            return ("", "frankenphp.exe not found.", 1);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FrankenPhpExe,
                Arguments = args,
                WorkingDirectory = FrankenPhpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (output, error, process.ExitCode);
        }
        catch (Exception ex)
        {
            return ("", ex.Message, 1);
        }
    }

    public static void UpdateSystemPathElevated(string versionDir)
    {
        var escapedBase = Regex.Escape(BasePath);
        var versionPattern = $@"^{escapedBase}\\[\d\.]+\\?$";

        var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var entries = machinePath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !Regex.IsMatch(e.Trim(), versionPattern))
            .Prepend(versionDir)
            .ToList();
        Environment.SetEnvironmentVariable("Path", string.Join(";", entries), EnvironmentVariableTarget.Machine);
    }

    public static bool RunElevated(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "",
                Arguments = arguments,
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task DownloadExtractEnablePackage(
        string downloadUrl, string packageName, string installDir, string extDir, string phpIni,
        string extensionName, string[] verifyModules, IProgress<string>? progress, string label)
    {
        var archive = Path.Combine(Path.GetTempPath(), $"{packageName}.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), packageName);

        try
        {
            progress?.Report($"Downloading {label} from {downloadUrl}...");
            LogWindow.LogDownload(downloadUrl);
            var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var stream = await response.Content.ReadAsStreamAsync())
            await using (var fs = File.Create(archive))
                await stream.CopyToAsync(fs);

            progress?.Report("Extracting...");
            LogWindow.LogExtract(extractPath);
            ZipFile.ExtractToDirectory(archive, extractPath, overwriteFiles: true);

            var mainDll = Path.Combine(extractPath, $"php_{extensionName}.dll");
            if (!File.Exists(mainDll))
                throw new FileNotFoundException($"php_{extensionName}.dll not found in downloaded package.");

            File.Copy(mainDll, Path.Combine(extDir, $"php_{extensionName}.dll"), overwrite: true);

            foreach (var dll in Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(dll) != $"php_{extensionName}.dll")
                    File.Copy(dll, Path.Combine(installDir, Path.GetFileName(dll)), overwrite: true);
            }

            progress?.Report("Enabling in php.ini...");
            EnableExtension(phpIni, extensionName);

            progress?.Report("Verifying...");
            var modules = GetLoadedModules(Path.GetFileName(installDir));
            foreach (var mod in verifyModules)
            {
                if (!modules.Contains(mod, StringComparer.OrdinalIgnoreCase))
                    throw new Exception($"{label} was installed but PHP could not load {mod}.");
            }

            progress?.Report($"{label} installed successfully.");
            LogWindow.LogSuccess($"{label} installed and verified.");
        }
        finally
        {
            CleanupTemp(archive, extractPath);
        }
    }

    private static void EnableExtension(string phpIniPath, string extensionName)
    {
        if (!File.Exists(phpIniPath))
            return;

        var content = File.ReadAllText(phpIniPath);
        var pattern = $@"(?m)^\s*;?\s*extension\s*=\s*(?:php_)?{Regex.Escape(extensionName)}(?:\.dll)?\s*$";

        content = Regex.IsMatch(content, pattern)
            ? Regex.Replace(content, pattern, $"extension={extensionName}")
            : content.TrimEnd() + $"\nextension={extensionName}\n";

        File.WriteAllText(phpIniPath, content, new UTF8Encoding(false));
    }

    private static string PatchIniValue(string content, string key, string replacement)
    {
        var pattern = $@"(?m)^\s*;?\s*{Regex.Escape(key)}\s*=.*$";
        return Regex.IsMatch(content, pattern)
            ? Regex.Replace(content, pattern, replacement)
            : content.TrimEnd() + "\n" + replacement + "\n";
    }

    private static void CleanupTemp(string archive, string extractPath)
    {
        try { if (File.Exists(archive)) File.Delete(archive); } catch { }
        try { if (Directory.Exists(extractPath)) Directory.Delete(extractPath, recursive: true); } catch { }
    }

    public static bool IsRunningAsAdmin()
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static (string Output, string Error, int ExitCode) RunPhp(string args, string? workingDir = null)
    {
        var activeVersion = GetActiveVersion();
        if (string.IsNullOrEmpty(activeVersion))
            return ("", "No active PHP version.", 1);

        string phpExe;

        if (activeVersion == "frankenphp")
        {
            // FrankenPHP embeds PHP — use the bundled php.exe
            phpExe = Path.Combine(FrankenPhpPath, "php.exe");
        }
        else
        {
            phpExe = Path.Combine(BasePath, activeVersion, "php.exe");
        }

        if (!File.Exists(phpExe))
            return ("", $"php.exe not found at {phpExe}", 1);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = phpExe,
                Arguments = args,
                WorkingDirectory = workingDir ?? Path.GetDirectoryName(phpExe),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return (output, error, process.ExitCode);
        }
        catch (Exception ex)
        {
            return ("", ex.Message, 1);
        }
    }
}
