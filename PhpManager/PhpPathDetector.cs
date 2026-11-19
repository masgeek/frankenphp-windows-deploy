using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>Finds a php.exe that is already on PATH. Kept free of PhpService's static state so it is unit-testable.</summary>
public static class PhpPathDetector
{
    /// <summary>Detects php.exe on PATH, skipping directories already managed under <paramref name="managedBasePath"/>.</summary>
    public static PhpVersionInfo? Detect(string managedBasePath) =>
        Find(
            EnumerateDirs(
                    Environment.GetEnvironmentVariable("Path"),
                    Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
                    Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine))
                .Where(d => !IsManaged(d, managedBasePath)),
            RunPhpForVersion);

    /// <summary>Splits PATH values into distinct, expanded, unquoted directories, preserving order.</summary>
    internal static IEnumerable<string> EnumerateDirs(params string?[] pathValues) =>
        pathValues
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .SelectMany(p => p!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(p => Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')))
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the first directory containing php.exe whose version can be read.</summary>
    internal static PhpVersionInfo? Find(IEnumerable<string> dirs, Func<string, string?> readVersionOutput)
    {
        foreach (var dir in dirs)
        {
            try
            {
                var exe = Path.Combine(dir, "php.exe");
                if (!File.Exists(exe))
                    continue;

                var output = readVersionOutput(exe);
                if (output == null)
                    continue;

                var m = Regex.Match(output, @"\d+\.\d+\.\d+");
                return new PhpVersionInfo
                {
                    Version = m.Success ? m.Value : "PHP",
                    InstallDir = dir,
                    Installed = true,
                    IsExternal = true
                };
            }
            catch
            {
                // unreadable PATH entry; try next
            }
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="dir"/> is a direct child of <paramref name="basePath"/>: those folders are
    /// scanned and listed by PhpManager itself (whatever they are named), so they are not "external".
    /// </summary>
    internal static bool IsManaged(string dir, string basePath)
    {
        var parent = Path.GetDirectoryName(dir.TrimEnd('\\'));
        return parent != null &&
               string.Equals(parent.TrimEnd('\\'), basePath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs php.exe and returns its version output, or null on failure/timeout.</summary>
    internal static string? ReadVersion(string exe) => RunPhpForVersion(exe);

    private static string? RunPhpForVersion(string exe)
    {
        var result = ProcessRunner.Run(exe, "-r \"echo PHP_VERSION;\"", timeoutMs: 5000);
        return result.TimedOut ? null : result.Output.Trim();
    }
}
