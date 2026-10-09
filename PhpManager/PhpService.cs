using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;
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

                await DownloadToFileAsync(url, archive, $"php-{version}-nts-x64.zip", progress);
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
            throw new Exception(
                "No compatible PHP versions found in any catalog. The catalogue scraper matches filenames " +
                "derived from the primary download URL, so check that both are consistent on the URLs page.");

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

            await DownloadToFileAsync(Urls.Cacert.DownloadUrl, cacertDest, "cacert.pem", progress);
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
        var majorMinor = $"{major}.{minor}";

        var (phpInfo, _, phpInfoExit) = RunPhp("-i", Path.Combine(BasePath, version));
        if (phpInfoExit != 0)
            throw new Exception("Unable to run php -i.");

        var threadSafety = phpInfo.Contains("Thread Safety => enabled") ? "ts" : "nts";
        var architecture = phpInfo.Contains("Architecture => x64") ? "x64" : "x86";

        // "PHP Extension Build => API20240924,VS17" gives the authoritative compiler.
        var compilerMatch = Regex.Match(phpInfo, @"PHP Extension Build => [^\r\n]*?,\s*(VC|VS)(\d+)",
            RegexOptions.IgnoreCase);

        string compiler;
        if (compilerMatch.Success)
        {
            var prefix = compilerMatch.Groups[1].Value.ToLowerInvariant();
            var number = compilerMatch.Groups[2].Value;
            compiler = $"{prefix}{number}";
        }
        else
        {
            var expected = int.Parse(major) switch
            {
                8 when int.Parse(minor) >= 4 => 17,
                8 => 16,
                _ => 15
            };
            compiler = $"{(expected >= 16 ? "vs" : "vc")}{expected}";
        }

        return (majorMinor, threadSafety, architecture, compiler);
    }

    // --- URL utilities ---

    /// <summary>Scrapes an Apache-style directory index and returns the version folders it lists.</summary>
    public static async Task<List<string>> GetAvailableExtensionVersionsAsync(string indexUrl, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(indexUrl))
            return [];

        progress?.Report($"Reading version index {indexUrl}...");
        LogWindow.Log($"Reading version index: {indexUrl}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var html = await Http.GetStringAsync(indexUrl, cts.Token);

        var versions = Regex.Matches(html, @"href=""(\d+(?:\.\d+)+(?:[A-Za-z]+\d*)?)/?""")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        versions = versions
            .OrderByDescending(v => IsPreRelease(v) ? 1 : 0)
            .ThenByDescending(v => ParseVersion(v))
            .ToList();

        progress?.Report($"Found {versions.Count} version(s).");
        return versions;
    }

    private static bool IsPreRelease(string version) =>
        Regex.IsMatch(version, @"(?i)(alpha|beta|rc|dev)");

    private static Version ParseVersion(string version)
    {
        var core = Regex.Match(version, @"^(\d+(?:\.\d+)*)");
        return core.Success && Version.TryParse(core.Groups[1].Value, out var v) ? v : new Version(0, 0);
    }

    public static async Task<(bool Ok, string Message)> TestUrlAsync(string url, string label)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (false, "URL is empty.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, "Not a valid absolute URL.");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(0, 0);

            var started = DateTime.UtcNow;
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;

            var size = response.Content.Headers.ContentLength;
            var sizeText = size.HasValue && size.Value > 0
                ? PhpService.FormatBytes(size.Value)
                : "size unknown";

            var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
            var ok = response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.PartialContent;

            var message = $"{label}: {status} — {sizeText}, {elapsed:F0} ms";
            if (ok)
                LogWindow.LogSuccess(message);
            else
                LogWindow.LogWarn(message);

            return (ok, message);
        }
        catch (OperationCanceledException)
        {
            var message = $"{label}: timed out after 30s — {url}";
            LogWindow.LogWarn(message);
            return (false, message);
        }
        catch (Exception ex)
        {
            var message = $"{label}: {ex.GetType().Name} — {ex.Message}";
            LogWindow.LogError(message);
            return (false, message);
        }
    }

    public static async Task<(int Passed, int Failed)> TestAllUrlsAsync(IProgress<string>? progress = null)
    {
        var urls = UrlDatabase.GetAll();
        var passed = 0;
        var failed = 0;

        foreach (var record in urls)
        {
            // Substitute placeholder tokens so the probe hits a real resource.
            var probe = record.UrlTemplate
                .Replace("{0}", "8.4.0")
                .Replace("{version}", "latest")
                .Replace("{package}", "probe")
                .Replace("{path}", "latest/download");

            if (!probe.Contains('{') && Uri.TryCreate(probe, UriKind.Absolute, out _))
            {
                progress?.Report($"Testing {record.Category}/{record.Key}...");
                var (ok, message) = await TestUrlAsync(probe, $"{record.Category}/{record.Key}");
                if (ok) passed++; else failed++;
            }
        }

        progress?.Report($"Tested {passed + failed} URL(s): {passed} reachable, {failed} failed.");
        return (passed, failed);
    }

    // --- Xdebug ---

    /// <summary>Detects the build details of an installed PHP version, as the Xdebug wizard would.</summary>
    public static XdebugEnvironment DetectXdebugEnvironment(string version)
    {
        var (output, error, exitCode) = RunPhp("-i", Path.Combine(BasePath, version));
        if (exitCode != 0)
            throw new Exception($"Unable to run php -i: {error}");

        var env = XdebugCompatibility.Parse(output);

        if (string.IsNullOrEmpty(env.PhpVersion))
        {
            var versionMatch = Regex.Match(output, @"^PHP (\d+\.\d+\.\d+)");
            if (versionMatch.Success)
            {
                env.PhpVersion = versionMatch.Groups[1].Value;
                env.PhpMajorMinor = env.PhpVersion[..3];
            }
        }

        if (env.WinCompiler == 0)
        {
            var (majorMinor, _, _, compiler) = GetPhpBuildInfo(version);
            env.PhpMajorMinor = string.IsNullOrEmpty(env.PhpMajorMinor) ? majorMinor : env.PhpMajorMinor;
            env.WinCompiler = int.TryParse(compiler.TrimStart('v', 'c'), out var wc) ? wc : 17;
        }

        if (string.IsNullOrEmpty(env.ExtensionDir))
            env.ExtensionDir = Path.Combine(BasePath, version, "ext");

        env.ConfigFile = Path.Combine(BasePath, version, "php.ini");

        return env;
    }

    public static bool IsXdebugInstalled(string version)
    {
        var extDir = Path.Combine(BasePath, version, "ext");
        return File.Exists(Path.Combine(extDir, "php_xdebug.dll"));
    }

    public static XdebugConfig ReadXdebugConfig(string version)
    {
        var config = new XdebugConfig();
        var phpIni = Path.Combine(BasePath, version, "php.ini");
        if (!File.Exists(phpIni))
            return config;

        var content = File.ReadAllText(phpIni);
        var section = ExtractIniSection(content, "xdebug");
        if (string.IsNullOrEmpty(section))
            return config;

        config.Enabled = Regex.IsMatch(section, @"(?mi)^\s*zend_extension\s*=\s*xdebug");
        config.Mode = ReadIniValue(section, "xdebug.mode") ?? config.Mode;
        config.StartWithRequest = ReadIniValue(section, "xdebug.start_with_request") ?? config.StartWithRequest;
        config.ClientHost = ReadIniValue(section, "xdebug.client_host") ?? config.ClientHost;
        config.ClientPort = int.TryParse(ReadIniValue(section, "xdebug.client_port"), out var port) ? port : config.ClientPort;
        config.IdeKey = ReadIniValue(section, "xdebug.idekey") ?? config.IdeKey;
        config.DiscoverClientHost = ReadIniValue(section, "xdebug.discover_client_host") ?? config.DiscoverClientHost;
        config.LogLevel = ReadIniValue(section, "xdebug.log_level") ?? config.LogLevel;
        config.LogFile = ReadIniValue(section, "xdebug.log") ?? config.LogFile;
        return config;
    }

    public static async Task InstallXdebugAsync(string version, string extensionVersion, IProgress<string>? progress = null)
    {
        var installDir = Path.Combine(BasePath, version);
        var phpExe = Path.Combine(installDir, "php.exe");
        var extDir = Path.Combine(installDir, "ext");

        if (!File.Exists(phpExe))
            throw new FileNotFoundException($"php.exe not found at {phpExe}");
        if (!Directory.Exists(extDir))
            throw new DirectoryNotFoundException($"Extension directory not found at {extDir}");

        var (majorMinor, threadSafety, architecture, compiler) = GetPhpBuildInfo(version);
        var package = $"php_xdebug-{extensionVersion}-{majorMinor}-{threadSafety}-{compiler}-{architecture}";
        var downloadUrl = Urls.Xdebug.DownloadUrl(extensionVersion, majorMinor, threadSafety, compiler, architecture);
        var archive = Path.Combine(Path.GetTempPath(), $"{package}.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), package);

        var env = new XdebugEnvironment
        {
            PhpMajorMinor = majorMinor,
            ThreadSafe = threadSafety == "ts",
            Architecture = architecture,
            WinCompiler = int.TryParse(compiler.TrimStart('v', 'c'), out var wc) ? wc : 17
        };

        try
        {
            progress?.Report($"Downloading Xdebug {extensionVersion} ({package}.zip)...");
            LogWindow.LogDownload(downloadUrl);

            await DownloadToFileAsync(downloadUrl, archive, $"{package}.zip", progress);

            progress?.Report($"Extracting {package}.zip...");
            LogWindow.LogExtract(extractPath);
            ZipFile.ExtractToDirectory(archive, extractPath, overwriteFiles: true);

            progress?.Report($"Installing {XdebugCompatibility.BuildExpectedDllName(env, extensionVersion)}...");
            InstallExtensionDll(extractPath, extDir, "xdebug",
                XdebugCompatibility.BuildExpectedDllName(env, extensionVersion));

            LogWindow.LogSuccess($"Xdebug {extensionVersion} installed as php_xdebug.dll in {extDir}.");
            progress?.Report($"Xdebug {extensionVersion} installed as php_xdebug.dll.");
        }
        catch (Exception ex) when (ex is HttpRequestException or FileNotFoundException)
        {
            throw new Exception(
                $"Xdebug {extensionVersion} is not available for PHP {majorMinor} {threadSafety.ToUpperInvariant()} {compiler} {architecture}. {ex.Message}", ex);
        }
        finally
        {
            CleanupTemp(archive, extractPath);
        }
    }

    /// <summary>
    /// Downloads a URL to disk, reporting verbose progress with transferred/total bytes.
    /// </summary>
    public static async Task DownloadToFileAsync(string url, string destination, string label, IProgress<string>? progress)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        var name = Path.GetFileName(destination);
        progress?.Report($"Downloading {label} ({(total.HasValue ? FormatBytes(total.Value) : "unknown size")})...");

        await using var source = await response.Content.ReadAsStreamAsync(cts.Token);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long received = 0;
        var lastReported = -1;

        int read;
        while ((read = await source.ReadAsync(buffer, cts.Token)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cts.Token);
            received += read;

            if (total is > 0)
            {
                var percent = (int)(received * 100 / total.Value);
                if (percent >= lastReported + 5 || percent == 100)
                {
                    lastReported = percent;
                    progress?.Report($"Downloading {name}: {percent}% ({FormatBytes(received)} of {FormatBytes(total.Value)})");
                }
            }
            else
            {
                progress?.Report($"Downloading {name}: {FormatBytes(received)}");
            }
        }

        progress?.Report($"Downloaded {name} ({FormatBytes(received)}).");
        LogWindow.LogSuccess($"Downloaded {label} ({FormatBytes(received)}).");
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    /// <summary>
    /// Locates an extension DLL inside an extracted package and installs it under its canonical,
    /// version-free name (e.g. php_xdebug-3.5.3-8.4-nts-vs17-x86_64.dll becomes php_xdebug.dll).
    /// </summary>
    /// <returns>The path of the installed DLL.</returns>
    public static string InstallExtensionDll(string extractPath, string extDir, string extensionName, string? expectedFileName = null)
    {
        var canonical = $"php_{extensionName}.dll";
        var target = Path.Combine(extDir, canonical);

        var candidates = Directory.GetFiles(extractPath, $"php_{extensionName}*.dll", SearchOption.AllDirectories);
        if (candidates.Length == 0)
            candidates = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);

        if (candidates.Length == 0)
            throw new FileNotFoundException($"{canonical} not found in downloaded package.");

        // 1. Exact canonical name, 2. the build-specific name we expect, 3. newest by file name.
        var source = candidates.FirstOrDefault(c => Path.GetFileName(c) == canonical)
            ?? candidates.FirstOrDefault(c => expectedFileName != null &&
                Path.GetFileName(c).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
            ?? candidates.OrderByDescending(c => Path.GetFileName(c), StringComparer.OrdinalIgnoreCase).First();

        File.Copy(source, target, overwrite: true);

        // Drop any other versioned copies of the same extension so only the canonical name remains.
        foreach (var stray in Directory.GetFiles(extDir, $"php_{extensionName}-*.dll", SearchOption.TopDirectoryOnly))
        {
            try { File.Delete(stray); } catch { }
        }

        LogWindow.Log($"Installed {Path.GetFileName(source)} as {canonical}.");
        return target;
    }

    public static void ConfigureXdebug(string version, XdebugConfig config)
    {
        var phpIni = Path.Combine(BasePath, version, "php.ini");
        if (!File.Exists(phpIni))
            throw new FileNotFoundException($"php.ini not found at {phpIni}");

        var content = File.ReadAllText(phpIni);
        var lines = new List<string> { "[xdebug]" };

        if (config.Enabled)
        {
            lines.Add("zend_extension=xdebug");
        }
        else
        {
            lines.Add(";zend_extension=xdebug");
            lines.Add("xdebug.mode=off");
        }

        lines.Add($"xdebug.mode={config.Mode}");
        lines.Add($"xdebug.start_with_request={config.StartWithRequest}");
        lines.Add($"xdebug.client_host={config.ClientHost}");
        lines.Add($"xdebug.client_port={config.ClientPort}");
        lines.Add($"xdebug.idekey={config.IdeKey}");
        lines.Add($"xdebug.discover_client_host={config.DiscoverClientHost}");
        lines.Add($"xdebug.log_level={config.LogLevel}");

        if (!string.IsNullOrWhiteSpace(config.LogFile))
            lines.Add($"xdebug.log={config.LogFile}");

        content = ReplaceIniSection(content, "xdebug", string.Join("\n", lines));
        File.WriteAllText(phpIni, content, new UTF8Encoding(false));
        LogWindow.LogSuccess($"Xdebug configuration applied to PHP {version}.");
    }

    public static bool VerifyXdebug(string version)
    {
        var (output, _, exitCode) = RunPhp("--version", Path.Combine(BasePath, version));
        return exitCode == 0 && output.Contains("Xdebug", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractIniSection(string content, string section)
    {
        var pattern = $@"(?ms)^\[{Regex.Escape(section)}\]\s*\r?\n(.*?)(?=^\[|\z)";
        var match = Regex.Match(content, pattern);
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string ReplaceIniSection(string content, string section, string body)
    {
        var pattern = $@"(?ms)^\[{Regex.Escape(section)}\]\s*\r?\n.*?(?=^\[|\z)";
        if (Regex.IsMatch(content, pattern))
            return Regex.Replace(content, pattern, body + "\n\n");

        return content.TrimEnd() + "\n\n" + body + "\n";
    }

    private static string? ReadIniValue(string content, string key)
    {
        var match = Regex.Match(content, $@"(?mi)^\s*;?\s*{Regex.Escape(key)}\s*=\s*(.+?)\s*$");
        return match.Success ? match.Groups[1].Value.Trim() : null;
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
            progress?.Report($"Downloading SQL Server drivers {driverVersion}...");
            LogWindow.LogDownload(downloadUrl);

            await DownloadToFileAsync(downloadUrl, archive, $"msphpsql-{driverVersion}.zip", progress);

            progress?.Report($"Extracting msphpsql-{driverVersion}.zip...");
            LogWindow.LogExtract(extractPath);
            ZipFile.ExtractToDirectory(archive, extractPath, overwriteFiles: true);

            progress?.Report("Installing as php_pdo_sqlsrv.dll / php_sqlsrv.dll...");
            try
            {
                InstallExtensionDll(extractPath, extDir, "pdo_sqlsrv");
                InstallExtensionDll(extractPath, extDir, "sqlsrv");
            }
            catch (FileNotFoundException ex)
            {
                throw new FileNotFoundException(
                    $"SQL Server drivers unavailable for PHP {majorMinor} {threadSafety.ToUpperInvariant()} {architecture} in release {driverVersion}. {ex.Message}", ex);
            }

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
            progress?.Report("Downloading FrankenPHP...");
            LogWindow.LogDownload(url);

            await DownloadToFileAsync(url, archive, "frankenphp.zip", progress);

            progress?.Report("Extracting frankenphp.zip...");
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

            await DownloadToFileAsync(url, archive, "servy-cli.zip", progress);

            progress?.Report("Extracting servy-cli.zip...");
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
            progress?.Report($"Downloading {label}...");
            LogWindow.LogDownload(downloadUrl);

            await DownloadToFileAsync(downloadUrl, archive, $"{packageName}.zip", progress);

            progress?.Report($"Extracting {packageName}.zip...");
            LogWindow.LogExtract(extractPath);
            ZipFile.ExtractToDirectory(archive, extractPath, overwriteFiles: true);

            progress?.Report($"Installing as php_{extensionName}.dll...");
            InstallExtensionDll(extractPath, extDir, extensionName);

            foreach (var dll in Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(dll);
                if (!fileName.StartsWith($"php_{extensionName}", StringComparison.OrdinalIgnoreCase) && fileName != $"php_{extensionName}.dll")
                    File.Copy(dll, Path.Combine(installDir, fileName), overwrite: true);
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
