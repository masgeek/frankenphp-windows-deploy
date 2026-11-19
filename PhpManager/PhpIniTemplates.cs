using System.IO;

namespace PhpManager;

public enum IniProfile
{
    Development,
    Production
}

/// <summary>Locates the php.ini template for a profile. Free of PhpService state so it is unit-testable.</summary>
public static class PhpIniTemplates
{
    public static string FileName(IniProfile profile) =>
        profile == IniProfile.Development ? "php.ini-development" : "php.ini-production";

    /// <summary>
    /// PHP zips ship php.ini-development and php.ini-production inside the install folder, so that copy wins.
    /// A same-named file next to the app is only a fallback (e.g. for FrankenPHP, which ships no templates).
    /// </summary>
    public static string? Resolve(string installDir, string appDir, IniProfile profile)
    {
        var name = FileName(profile);
        foreach (var dir in new[] { installDir, appDir })
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
