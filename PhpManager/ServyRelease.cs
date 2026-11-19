using System.IO;
using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>
/// Finds the Servy CLI package on the project's GitHub releases page (https://github.com/aelassas/servy/releases).
/// Asset names carry the version (servy-10.9-net48-x64-portable.7z), so the latest one is looked up through the
/// GitHub API instead of being hard-coded.
/// </summary>
public static class ServyRelease
{
    public const string DefaultReleasesUrl = "https://github.com/aelassas/servy/releases";
    public const string DefaultRepositoryUrl = "https://github.com/aelassas/servy";

    /// <summary>The default URLs shipped by earlier versions (wrong repository and a file that does not exist).</summary>
    public static readonly (string Category, string Key, string OldUrl, string NewUrl)[] LegacyDefaults =
    [
        ("Servy", "Download", "https://github.com/nicholasgasior/servy/releases/latest/download/servy-cli-windows-amd64.zip", DefaultReleasesUrl),
        ("Documentation", "Servy", "https://github.com/nicholasgasior/servy", DefaultRepositoryUrl)
    ];

    /// <summary>True when the URL is a GitHub releases page rather than a direct link to a package.</summary>
    public static bool IsReleasesPage(string url) =>
        Regex.IsMatch(url.Trim().TrimEnd('/'), @"^https?://github\.com/[^/]+/[^/]+/releases(/latest|/tag/[^/]+)?$", RegexOptions.IgnoreCase);

    /// <summary>API address of the latest (or the named) release for a releases page; null if it is not one.</summary>
    public static string? ApiUrl(string releasesUrl)
    {
        var m = Regex.Match(releasesUrl.Trim().TrimEnd('/'),
            @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/releases(?:/latest|/tag/(?<tag>[^/]+))?$", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        var baseUrl = $"https://api.github.com/repos/{m.Groups["owner"].Value}/{m.Groups["repo"].Value}/releases";
        return m.Groups["tag"].Success ? $"{baseUrl}/tags/{m.Groups["tag"].Value}" : $"{baseUrl}/latest";
    }

    /// <summary>
    /// The package to install: the small framework-dependent x64 portable build (it holds servy-cli.exe and runs on the
    /// .NET Framework 4.8 that ships with Windows 10/11), else the self-contained x64 portable. Installers, ARM and
    /// SBOM files are never chosen.
    /// </summary>
    public static string? PickAsset(IEnumerable<string> assetNames)
    {
        var names = assetNames.ToList();
        return names.FirstOrDefault(n => Regex.IsMatch(n, @"net48-x64-portable\.(7z|zip)$", RegexOptions.IgnoreCase))
               ?? names.FirstOrDefault(n => Regex.IsMatch(n, @"^(?!.*(net48|arm64)).*x64-portable\.(7z|zip)$", RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Copies the whole folder that holds servy-cli.exe (it needs the DLLs next to it) into <paramref name="destDir"/>.
    /// Returns the path of the installed servy-cli.exe.
    /// </summary>
    public static string InstallFrom(string extractedRoot, string destDir)
    {
        var exe = Directory.GetFiles(extractedRoot, "servy-cli.exe", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new FileNotFoundException("servy-cli.exe not found in the downloaded package.");

        CopyTree(Path.GetDirectoryName(exe)!, destDir);
        return Path.Combine(destDir, "servy-cli.exe");
    }

    private static void CopyTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(source))
            CopyTree(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
