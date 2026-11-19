using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>
/// Picks the right DLL out of a package that contains many builds (for example the Microsoft SQL Server drivers ship
/// php_pdo_sqlsrv_82_nts.dll, _82_ts.dll, _83_nts.dll ... under x64 and x86 folders). A wrong build fails to load.
/// </summary>
public static class ExtensionDllSelector
{
    /// <summary>What a file name or path says about the build; null means "not stated".</summary>
    public sealed record Variant(string Path, string? MajorMinor, string? ThreadSafety, string? Arch)
    {
        public int Specificity => (MajorMinor != null ? 1 : 0) + (ThreadSafety != null ? 1 : 0) + (Arch != null ? 1 : 0);

        public string Describe() =>
            $"PHP {MajorMinor ?? "any"} {(ThreadSafety ?? "any").ToUpperInvariant()} {Arch ?? "any"}";
    }

    public static Variant Parse(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var segments = path.Replace('/', '\\').Split('\\').Select(s => s.ToLowerInvariant()).ToList();

        string? majorMinor = null;
        var compact = Regex.Match(name, @"(?:^|[_-])(\d)(\d{1,2})(?=[_-]|$)");
        var dotted = Regex.Match(name, @"(?:^|[_-])(\d)\.(\d{1,2})(?=[_-]|$)");
        if (dotted.Success)
            majorMinor = $"{dotted.Groups[1].Value}.{dotted.Groups[2].Value}";
        else if (compact.Success)
            majorMinor = $"{compact.Groups[1].Value}.{compact.Groups[2].Value}";

        var tokens = Regex.Split(name, @"[_\-.]");
        string? ts = tokens.Contains("nts") ? "nts" : tokens.Contains("ts") ? "ts" : null;

        string? arch = tokens.Contains("x64") ? "x64" : tokens.Contains("x86") ? "x86" : null;
        arch ??= segments.Contains("x64") ? "x64" : segments.Contains("x86") ? "x86" : null;

        return new Variant(path, majorMinor, ts, arch);
    }

    /// <summary>
    /// The best candidate for the running PHP: every stated attribute must match, and the most specific file wins.
    /// Returns null when nothing is compatible (so the caller can say what the package does offer).
    /// </summary>
    public static string? Choose(IEnumerable<string> paths, string majorMinor, string threadSafety, string arch) =>
        paths.Select(Parse)
            .Where(v => (v.MajorMinor == null || v.MajorMinor == majorMinor) &&
                        (v.ThreadSafety == null || v.ThreadSafety.Equals(threadSafety, StringComparison.OrdinalIgnoreCase)) &&
                        (v.Arch == null || v.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(v => v.Specificity)
            .ThenBy(v => v.Path, StringComparer.OrdinalIgnoreCase)
            .Select(v => v.Path)
            .FirstOrDefault();

    /// <summary>Short list of the builds a package offers, for error messages.</summary>
    public static string Available(IEnumerable<string> paths) =>
        string.Join(", ", paths.Select(Parse).Select(v => v.Describe()).Distinct().OrderBy(s => s));

    /// <summary>
    /// Copies the right DLL from an extracted package into the ext folder as php_&lt;name&gt;.dll and returns the source file.
    /// With <paramref name="build"/> the DLL must match this PHP's version, thread-safety and architecture; without it the
    /// exact canonical name, then <paramref name="expectedFileName"/>, then the newest name is used.
    /// </summary>
    public static string CopyBest(string extractPath, string extDir, string extensionName, string? expectedFileName,
        (string MajorMinor, string ThreadSafety, string Architecture)? build)
    {
        var canonical = $"php_{extensionName}.dll";
        var target = System.IO.Path.Combine(extDir, canonical);

        var candidates = System.IO.Directory.GetFiles(extractPath, $"php_{extensionName}*.dll", System.IO.SearchOption.AllDirectories);
        if (candidates.Length == 0)
            candidates = System.IO.Directory.GetFiles(extractPath, "*.dll", System.IO.SearchOption.AllDirectories);

        if (candidates.Length == 0)
            throw new System.IO.FileNotFoundException($"{canonical} not found in downloaded package.");

        string source;
        if (build is { } b)
        {
            // Packages such as the SQL Server drivers hold a DLL per PHP version, thread-safety and architecture.
            // Pick the one that matches this PHP exactly; a wrong build fails to load.
            source = Choose(candidates, b.MajorMinor, b.ThreadSafety, b.Architecture)
                ?? throw new System.IO.FileNotFoundException(
                    $"This package has no {canonical} build for PHP {b.MajorMinor} {b.ThreadSafety.ToUpperInvariant()} {b.Architecture}. " +
                    $"It offers: {Available(candidates)}. Try another release on the URLs page or a different PHP version.");
        }
        else
        {
            source = candidates.FirstOrDefault(c => System.IO.Path.GetFileName(c) == canonical)
                ?? candidates.FirstOrDefault(c => expectedFileName != null &&
                    System.IO.Path.GetFileName(c).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
                ?? candidates.OrderByDescending(c => System.IO.Path.GetFileName(c), StringComparer.OrdinalIgnoreCase).First();
        }

        System.IO.File.Copy(source, target, overwrite: true);

        // Drop any other versioned copies of the same extension so only the canonical name remains.
        foreach (var stray in System.IO.Directory.GetFiles(extDir, $"php_{extensionName}-*.dll", System.IO.SearchOption.TopDirectoryOnly))
        {
            try { System.IO.File.Delete(stray); } catch { /* best effort */ }
        }

        return source;
    }
}
