namespace PhpManager;

public enum LaravelProfile
{
    Local,
    Production
}

/// <summary>Recommended php.ini values for running Laravel, for local development or production.</summary>
public static class LaravelPreset
{
    private static readonly (string Key, string Local, string Production)[] Values =
    [
        ("memory_limit", "512M", "512M"),
        ("upload_max_filesize", "64M", "64M"),
        ("post_max_size", "64M", "64M"),
        ("max_execution_time", "120", "60"),
        ("max_input_vars", "5000", "5000"),
        ("realpath_cache_size", "16M", "16M"),
        ("realpath_cache_ttl", "600", "600"),
        ("opcache.enable", "On", "On"),
        ("opcache.enable_cli", "Off", "Off"),
        ("opcache.memory_consumption", "256", "256"),
        ("opcache.max_accelerated_files", "20000", "20000"),
        ("opcache.validate_timestamps", "On", "Off"),
        ("opcache.revalidate_freq", "0", "0"),
        ("display_errors", "On", "Off"),
        ("error_reporting", "E_ALL", "E_ALL & ~E_DEPRECATED & ~E_STRICT"),
        ("log_errors", "On", "On"),
        ("zend.assertions", "1", "-1"),
        ("expose_php", "On", "Off"),
        ("date.timezone", "UTC", "UTC")
    ];

    /// <summary>Directive values for the profile. date.timezone is only applied if you have not set one yourself.</summary>
    public static Dictionary<string, string> Settings(LaravelProfile profile) =>
        Values.ToDictionary(v => v.Key, v => profile == LaravelProfile.Local ? v.Local : v.Production);

    public static string Describe(LaravelProfile profile) => profile == LaravelProfile.Local
        ? "Local development: errors shown, OPcache re-checks files, assertions on."
        : "Production: errors logged not shown, OPcache never re-checks files (restart PHP after deploys), assertions off.";
}
