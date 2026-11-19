using System.IO;

namespace PhpManager;

public enum DoctorStatus
{
    Pass,
    Warn,
    Fail
}

/// <param name="FixId">Identifier of an automatic fix the UI can offer, or null.</param>
public sealed record DoctorResult(string Name, DoctorStatus Status, string Detail, string? FixId = null)
{
    public string Icon => Status switch { DoctorStatus.Pass => "✓", DoctorStatus.Warn => "!", _ => "✗" };
    public string FixLabel => FixId switch
    {
        "cacert" => "Install CA certificate",
        "disable-broken" => "Disable broken extensions",
        "reapply-path" => "Fix PATH",
        "install-composer" => "Install Composer",
        "apply-profile" => "Create php.ini",
        "dedupe-ini" => "Remove duplicate lines",
        "set-active" => "Set a version active",
        _ => ""
    };
    public bool CanFix => FixId != null;
}

/// <summary>The individual rules behind the health check, free of process and file-system state where possible.</summary>
public static class DoctorEvaluators
{
    public static DoctorResult PhpSupportCheck(string version, DateOnly today)
    {
        var status = PhpSupport.StatusOf(version, today);
        var end = PhpSupport.SecurityEnd(version);
        return status switch
        {
            SupportStatus.Active => new("PHP version support", DoctorStatus.Pass, $"PHP {PhpSupport.MajorMinor(version)} is actively supported."),
            SupportStatus.SecurityOnly => new("PHP version support", DoctorStatus.Warn,
                $"PHP {PhpSupport.MajorMinor(version)} only gets security fixes (until {end:yyyy-MM-dd}). Plan an upgrade."),
            SupportStatus.EndOfLife => new("PHP version support", DoctorStatus.Fail,
                $"PHP {PhpSupport.MajorMinor(version)} reached end of life on {end:yyyy-MM-dd} and no longer gets security fixes."),
            _ => new("PHP version support", DoctorStatus.Warn, $"No support information for PHP {PhpSupport.MajorMinor(version)}.")
        };
    }

    /// <summary>extension_dir must exist; "ext" is relative to the install folder.</summary>
    public static DoctorResult ExtensionDirCheck(string iniContent, string installDir, Func<string, bool> dirExists)
    {
        var value = PhpIniEditor.GetDirective(iniContent, "extension_dir");
        if (string.IsNullOrWhiteSpace(value))
            return new("extension_dir", DoctorStatus.Warn, "extension_dir is not set in php.ini, so extensions may not load.", "apply-profile");

        var resolved = Path.IsPathRooted(value) ? value : Path.Combine(installDir, value);
        return dirExists(resolved)
            ? new("extension_dir", DoctorStatus.Pass, $"Points to {resolved}.")
            : new("extension_dir", DoctorStatus.Fail, $"extension_dir points to {resolved}, which does not exist.", "apply-profile");
    }

    /// <summary>The same extension on two lines makes PHP warn "Module already loaded" and makes edits ambiguous.</summary>
    public static DoctorResult DuplicateExtensionsCheck(string iniContent)
    {
        var dups = PhpIniEditor.FindDuplicateExtensions(iniContent);
        return dups.Count == 0
            ? new("Duplicate extensions", DoctorStatus.Pass, "Each extension appears once in php.ini.")
            : new("Duplicate extensions", DoctorStatus.Warn,
                $"{dups.Count} extension(s) are listed more than once: {string.Join(", ", dups)}.", "dedupe-ini");
    }

    public static DoctorResult CaCertCheck(string iniContent, Func<string, bool> fileExists)
    {
        var cafile = PhpIniEditor.GetDirective(iniContent, "openssl.cafile") ?? PhpIniEditor.GetDirective(iniContent, "curl.cainfo");
        if (string.IsNullOrWhiteSpace(cafile))
            return new("CA certificates", DoctorStatus.Warn,
                "No CA bundle configured; HTTPS requests (Composer, curl) may fail certificate checks.", "cacert");

        return fileExists(cafile)
            ? new("CA certificates", DoctorStatus.Pass, $"Using {cafile}.")
            : new("CA certificates", DoctorStatus.Fail, $"CA bundle {cafile} is configured but the file is missing.", "cacert");
    }

    /// <summary>
    /// The first php.exe on PATH is the one a terminal runs. It should be the active version; extra copies are a warning.
    /// </summary>
    public static DoctorResult PathCheck(IEnumerable<string> pathDirs, string? activeDir, Func<string, bool> hasPhpExe)
    {
        var withPhp = pathDirs.Where(hasPhpExe).Select(d => d.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (withPhp.Count == 0)
            return new("PATH", DoctorStatus.Warn, "php.exe is not on PATH; a terminal won't find php.", "reapply-path");

        if (activeDir == null)
            return new("PATH", DoctorStatus.Warn, $"php.exe on PATH is {withPhp[0]}, but no active version is selected.", "set-active");

        var active = activeDir.TrimEnd('\\');
        if (!string.Equals(withPhp[0], active, StringComparison.OrdinalIgnoreCase))
            return new("PATH", DoctorStatus.Fail,
                $"A terminal runs php from {withPhp[0]}, not the active version at {active}.", "reapply-path");

        return withPhp.Count > 1
            ? new("PATH", DoctorStatus.Warn, $"Active PHP wins, but {withPhp.Count - 1} other php.exe folder(s) are also on PATH: {string.Join("; ", withPhp.Skip(1))}.")
            : new("PATH", DoctorStatus.Pass, $"php resolves to the active version ({active}).");
    }

    public static DoctorResult ComposerCheck(IEnumerable<string> pathDirs, Func<string, bool> fileExists)
    {
        var names = new[] { "composer.bat", "composer.cmd", "composer.exe", "composer.phar" };
        var found = pathDirs.FirstOrDefault(d => names.Any(n => fileExists(Path.Combine(d, n))));
        return found != null
            ? new("Composer", DoctorStatus.Pass, $"Found in {found}.")
            : new("Composer", DoctorStatus.Warn, "Composer is not on PATH.", "install-composer");
    }
}
