using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>Installs and updates Composer as composer.phar plus a composer.bat shim, and puts it on the user PATH.</summary>
public static class ComposerService
{
    public static string Dir => Path.Combine(PhpService.BasePath, "composer");
    public static string PharPath => Path.Combine(Dir, "composer.phar");
    public static string ShimPath => Path.Combine(Dir, "composer.bat");
    public static string HomePath => Path.Combine(Dir, "home");

    /// <summary>The shim finds php through PATH, so it always uses whichever PHP is active.</summary>
    public static string ShimContent() => "@echo off\r\nphp \"%~dp0composer.phar\" %*\r\n";

    public static bool IsInstalled() => File.Exists(PharPath);

    /// <summary>Parses "Composer version 2.8.4 2025-..." (output may start with warnings).</summary>
    public static string? ParseVersion(string? output)
    {
        var m = Regex.Match(output ?? "", @"Composer(?: version)?\s+(\d+\.\d+\.\d+\S*)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string? GetVersion()
    {
        if (!IsInstalled())
            return null;

        var active = PhpService.GetActiveVersion();
        if (string.IsNullOrEmpty(active))
            return null;

        var phpExe = Path.Combine(active == "frankenphp" ? PhpService.FrankenPhpPath : Path.Combine(PhpService.BasePath, active), "php.exe");
        if (!File.Exists(phpExe))
            return null;

        var result = ProcessRunner.Run(phpExe, $"\"{PharPath}\" --version --no-ansi", timeoutMs: 30_000);
        return ParseVersion(result.Combined);
    }

    /// <summary>Downloads the latest stable composer.phar (also used to update) and makes sure the shim is on PATH.</summary>
    public static async Task InstallAsync(IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(Dir);
        var temp = PharPath + ".download";

        progress?.Report("Downloading Composer...");
        await PhpService.DownloadToFileAsync(Urls.Composer.DownloadUrl, temp, "composer.phar", progress);

        if (new FileInfo(temp).Length < 100_000)
        {
            File.Delete(temp);
            throw new InvalidDataException("The downloaded Composer file is too small to be composer.phar.");
        }

        File.Move(temp, PharPath, overwrite: true);
        File.WriteAllText(ShimPath, ShimContent(), new UTF8Encoding(false));

        progress?.Report("Adding Composer to PATH...");
        EnsureOnUserPath();
        progress?.Report("Composer installed.");
    }

    public static void EnsureOnUserPath()
    {
        var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        if (!PathEditor.Contains(user, Dir))
            Environment.SetEnvironmentVariable("Path", string.IsNullOrEmpty(user) ? Dir : user.TrimEnd(';') + ";" + Dir, EnvironmentVariableTarget.User);

        var process = Environment.GetEnvironmentVariable("Path");
        if (!PathEditor.Contains(process, Dir))
            Environment.SetEnvironmentVariable("Path", (process ?? "").TrimEnd(';') + ";" + Dir);
    }

    /// <summary>Gives Composer a stable home/cache folder under the PHP base path.</summary>
    public static void SetComposerHome()
    {
        Directory.CreateDirectory(HomePath);
        Environment.SetEnvironmentVariable("COMPOSER_HOME", HomePath, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("COMPOSER_HOME", HomePath);
    }

    public static string? CurrentComposerHome() => Environment.GetEnvironmentVariable("COMPOSER_HOME", EnvironmentVariableTarget.User);
}
