using System.IO;
using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>
/// Decides, for every extension enabled in php.ini, whether PHP loaded it, failed to load it, or has no DLL for it.
/// Covers both <c>extension=</c> and <c>zend_extension=</c> (OPcache, Xdebug) lines.
/// </summary>
public static class ExtensionHealth
{
    private static readonly Regex PlainLine = new(
        @"(?m)^[ \t]*extension[ \t]*=[ \t]*""?(?:[^""\r\n;]*[\\/])?(?:(?i:php_))?(?<name>\w+)(?:(?i:\.dll))?""?[ \t]*\r?$", RegexOptions.Compiled);

    private static readonly Regex ZendLine = new(
        @"(?m)^[ \t]*zend_extension[ \t]*=[ \t]*""?(?<path>[^""\r\n;]*?)(?:(?i:php_))?(?<name>\w+)(?:(?i:\.dll))?""?[ \t]*\r?$", RegexOptions.Compiled);

    public static PhpService.VerifyResult Classify(
        string iniContent,
        IReadOnlySet<string> availableDlls,
        IReadOnlyCollection<string> loadedModules,
        IReadOnlyDictionary<string, string> loadFailures,
        bool timedOut,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var loaded = new List<string>();
        var failed = new List<string>();
        var noDll = new List<string>();
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // extension=name: PHP lists the module in `php -m` when it loaded.
        foreach (Match m in PlainLine.Matches(iniContent))
        {
            var ext = m.Groups["name"].Value;
            if (loaded.Contains(ext, StringComparer.OrdinalIgnoreCase) || failed.Contains(ext, StringComparer.OrdinalIgnoreCase) ||
                noDll.Contains(ext, StringComparer.OrdinalIgnoreCase))
                continue;

            if (loadedModules.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                loaded.Add(ext);
            }
            else if (availableDlls.Contains(ext))
            {
                failed.Add(ext);
                reasons[ext] = loadFailures.TryGetValue(ext, out var why)
                    ? why
                    : timedOut ? "php -m timed out" : "DLL present but PHP did not load it";
            }
            else
            {
                noDll.Add(ext);
                reasons[ext] = $"php_{ext}.dll is not in the ext folder";
            }
        }

        // zend_extension=name: modules such as "Zend OPcache" and "Xdebug" are not named like the DLL, so a missing DLL
        // or a "Failed loading Zend extension" warning decides instead.
        foreach (Match m in ZendLine.Matches(iniContent))
        {
            var ext = m.Groups["name"].Value;
            if (loaded.Contains(ext, StringComparer.OrdinalIgnoreCase) || failed.Contains(ext, StringComparer.OrdinalIgnoreCase) ||
                noDll.Contains(ext, StringComparer.OrdinalIgnoreCase))
                continue;

            var path = m.Groups["path"].Value.Trim();
            var dllExists = availableDlls.Contains(ext) ||
                            (path.Length > 0 && (fileExists(path + $"php_{ext}.dll") || fileExists(path + $"{ext}.dll")));

            if (!dllExists)
            {
                noDll.Add(ext);
                reasons[ext] = $"php_{ext}.dll is not in the ext folder; download it or disable {ext}";
            }
            else if (loadFailures.TryGetValue(ext, out var why))
            {
                failed.Add(ext);
                reasons[ext] = why;
            }
            else
            {
                loaded.Add(ext);
            }
        }

        return new PhpService.VerifyResult(loaded, failed, noDll, reasons, timedOut);
    }
}
