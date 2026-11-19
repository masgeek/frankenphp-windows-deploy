using System.IO;

namespace PhpManager;

/// <summary>
/// Finds servy-cli.exe without running it and without trusting this process's own PATH, which does not change
/// after the app starts (so a Servy installed or added to PATH while PhpManager is open would stay invisible).
/// </summary>
public static class ServyLocator
{
    public const string ExeName = "servy-cli.exe";

    /// <summary>Folders Servy's installer and portable builds commonly end up in.</summary>
    public static List<string> KnownDirs(string programFiles, string programFilesX86, string localAppData) =>
        new[]
        {
            Path.Combine(programFiles, "Servy"),
            Path.Combine(programFilesX86, "Servy"),
            Path.Combine(localAppData, "Programs", "Servy"),
            Path.Combine(localAppData, "Servy")
        }
        .Where(d => !string.IsNullOrWhiteSpace(Path.GetDirectoryName(d)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// First existing servy-cli.exe. Order: the copy PhpManager installed itself, then PATH (freshly read), then the
    /// usual install folders. Returns the full path, or null when none exists.
    /// </summary>
    public static string? Find(IEnumerable<string> ownDirs, IEnumerable<string> pathDirs, IEnumerable<string> knownDirs, Func<string, bool> fileExists)
    {
        foreach (var dir in ownDirs.Concat(pathDirs).Concat(knownDirs))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            var candidate = Path.Combine(dir.Trim().Trim('"'), ExeName);
            if (fileExists(candidate))
                return candidate;
        }

        return null;
    }
}
