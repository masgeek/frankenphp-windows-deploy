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

    private static readonly string[] DownloadUrls =
    [
        "https://windows.php.net/downloads/releases/php-{0}-nts-Win32-vs17-x64.zip",
        "https://downloads.php.net/~windows/releases/php-{0}-nts-Win32-vs17-x64.zip",
        "https://downloads.php.net/~windows/releases/archives/php-{0}-nts-Win32-vs17-x64.zip"
    ];

    private static readonly string[] CatalogUrls =
    [
        "https://windows.php.net/downloads/releases/",
        "https://downloads.php.net/~windows/releases/",
        "https://downloads.php.net/~windows/releases/archives/"
    ];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    static PhpService()
    {
        var path = ResolveCacheFile(".php-install-path.cache");
        if (path != null)
        {
            var cached = File.ReadAllText(path).Trim();
            if (!string.IsNullOrEmpty(cached))
                BasePath = cached;
        }
    }

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
        if (!string.IsNullOrEmpty(version) && Directory.Exists(Path.Combine(BasePath, version)))
            return version;

        return "";
    }

    public static void SetActiveVersion(string version, bool addToSystemPath = false)
    {
        var versionDir = Path.Combine(BasePath, version);
        if (!Directory.Exists(versionDir))
            throw new DirectoryNotFoundException($"PHP {version} not found at {versionDir}");

        var phpExe = Path.Combine(versionDir, "php.exe");
        if (!File.Exists(phpExe))
            throw new FileNotFoundException($"php.exe not found in {versionDir}");

        File.WriteAllText(Path.Combine(BasePath, ".php-active-version"), version, new UTF8Encoding(false));

        var versionPattern = $@"^{Regex.Escape(BasePath)}\\[\d\.]+\\?$";

        // User PATH
        var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var userEntries = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !Regex.IsMatch(e.Trim(), versionPattern))
            .Prepend(versionDir)
            .ToList();
        Environment.SetEnvironmentVariable("Path", string.Join(";", userEntries), EnvironmentVariableTarget.User);

        // Process PATH
        var processEntries = (Environment.GetEnvironmentVariable("Path") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !Regex.IsMatch(e.Trim(), versionPattern))
            .Prepend(versionDir)
            .ToList();
        Environment.SetEnvironmentVariable("Path", string.Join(";", processEntries));

        Environment.SetEnvironmentVariable("PHPRC", Path.Combine(versionDir, "php.ini"));

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

        return Directory.GetDirectories(BasePath)
            .Select(dir => Path.GetFileName(dir))
            .Where(name => Regex.IsMatch(name, @"^\d+\.\d+\.\d+$"))
            .Select(name => new PhpVersionInfo
            {
                Version = name,
                InstallDir = Path.Combine(BasePath, name),
                Installed = File.Exists(Path.Combine(BasePath, name, "php.exe")),
                Active = name == activeVersion
            })
            .OrderByDescending(v => Version.Parse(v.Version))
            .ToList();
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
                var response = await Http.GetAsync(url);
                response.EnsureSuccessStatusCode();
                await using var fs = File.Create(archive);
                await response.Content.CopyToAsync(fs);
                progress?.Report("Download complete.");
                break;
            }
            catch (Exception ex)
            {
                progress?.Report($"Failed: {ex.Message}");
            }
        }

        if (!File.Exists(archive))
            throw new Exception($"Unable to download PHP {version} from any configured URL.");

        progress?.Report("Extracting...");
        ZipFile.ExtractToDirectory(archive, targetDir, overwriteFiles: true);
        File.Delete(archive);
        progress?.Report("Extraction complete.");
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
                foreach (Match m in Regex.Matches(html, @"php-(\d+\.\d+\.\d+)-nts-Win32-vs17-x64\.zip", RegexOptions.IgnoreCase))
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
            var response = await Http.GetAsync("https://curl.se/ca/cacert.pem");
            response.EnsureSuccessStatusCode();
            await using var fs = File.Create(cacertDest);
            await response.Content.CopyToAsync(fs);
        }

        if (!File.Exists(phpIni))
            throw new FileNotFoundException($"php.ini not found at {phpIni}");

        var content = File.ReadAllText(phpIni);
        var cacertPath = cacertDest.Replace("\\", "/");

        content = PatchIniValue(content, "curl.cainfo", $"curl.cainfo = \"{cacertPath}\"");
        content = PatchIniValue(content, "openssl.cafile", $"openssl.cafile = \"{cacertPath}\"");

        File.WriteAllText(phpIni, content, new UTF8Encoding(false));
        progress?.Report("CA certificate configured.");
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
        var downloadUrl = $"https://windows.php.net/downloads/pecl/releases/redis/{extensionVersion}/{package}.zip";

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
        var downloadUrl = $"https://github.com/microsoft/msphpsql/releases/download/v{driverVersion}/Windows_{driverVersion}RTW.zip";
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

    // --- Helpers ---

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
            var response = await Http.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();
            await using (var fs = File.Create(archive))
                await response.Content.CopyToAsync(fs);

            progress?.Report("Extracting...");
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

        var phpExe = Path.Combine(BasePath, activeVersion, "php.exe");
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
