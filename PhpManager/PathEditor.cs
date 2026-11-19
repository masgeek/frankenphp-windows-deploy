using System.IO;
namespace PhpManager;

/// <summary>Pure PATH string helpers (no environment access) so they can be unit-tested.</summary>
public static class PathEditor
{
    private static string Norm(string entry) =>
        Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"')).TrimEnd('\\', '/');

    public static bool Contains(string? path, string dir) =>
        (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(e => string.Equals(Norm(e), Norm(dir), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True for a PATH entry that is one of PhpManager's PHP version folders: a direct child of
    /// <paramref name="basePath"/> (never frankenphp) that either holds php.exe or is named like a version (8.4.27,
    /// so stale entries for deleted versions are still cleaned up). Other children such as composer are left alone.
    /// </summary>
    public static bool IsManagedPhpEntry(string entry, string basePath, Func<string, bool> hasPhpExe)
    {
        var dir = Norm(entry);
        var parent = Path.GetDirectoryName(dir);
        if (parent == null || !string.Equals(parent.TrimEnd('\\'), basePath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return false;

        var name = Path.GetFileName(dir);
        if (name.Equals("frankenphp", StringComparison.OrdinalIgnoreCase))
            return false;

        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[\d.]+$") || hasPhpExe(dir);
    }

    /// <summary>Removes every entry equal to <paramref name="dir"/> (case/trailing-slash/quote insensitive).</summary>
    public static string RemoveEntry(string? path, string dir) =>
        string.Join(";", (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !string.Equals(Norm(e), Norm(dir), StringComparison.OrdinalIgnoreCase)));
}
