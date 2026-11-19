using System.Text.RegularExpressions;

namespace PhpManager;

public enum IniValueKind
{
    /// <summary>Byte size such as 128M, 2G, 512K, a plain number, or -1 (unlimited).</summary>
    Size,
    /// <summary>Whole number (seconds, counts); -1 allowed where the setting accepts it.</summary>
    Int,
    /// <summary>On / Off.</summary>
    Bool,
    /// <summary>Free text (timezone, error_reporting expression, paths).</summary>
    Text
}

/// <param name="Choices">Preset values offered in the dropdown, so most edits need no typing.</param>
/// <param name="AllowCustom">When true the dropdown also has a "Custom..." entry that reveals a text box.</param>
public sealed record IniSetting(
    string Key, string Group, string Label, IniValueKind Kind, string Default, string Hint = "",
    string[]? Choices = null, bool AllowCustom = true)
{
    /// <summary>The dropdown items (Bool is always On/Off with no custom entry).</summary>
    public string[] Options => Kind == IniValueKind.Bool ? ["On", "Off"] : Choices ?? [];
    public bool CanBeCustom => Kind != IniValueKind.Bool && AllowCustom;
}

/// <summary>The common php.ini directives worth editing from the UI, plus value validation.</summary>
public static class IniSettingDefs
{
    private static readonly string[] Sizes = ["1M", "2M", "8M", "16M", "32M", "64M", "128M", "256M", "512M", "1G", "2G"];
    private static readonly string[] MemorySizes = ["64M", "128M", "256M", "512M", "1G", "2G", "-1"];

    /// <summary>Common IANA zones (PHP's date.timezone needs these names, not Windows ones).</summary>
    public static readonly string[] TimeZones =
    [
        "UTC",
        "Africa/Abidjan", "Africa/Accra", "Africa/Addis_Ababa", "Africa/Algiers", "Africa/Cairo", "Africa/Casablanca",
        "Africa/Dakar", "Africa/Dar_es_Salaam", "Africa/Harare", "Africa/Johannesburg", "Africa/Kampala",
        "Africa/Kinshasa", "Africa/Lagos", "Africa/Lusaka", "Africa/Nairobi", "Africa/Tunis",
        "America/Bogota", "America/Chicago", "America/Denver", "America/Los_Angeles", "America/Mexico_City",
        "America/New_York", "America/Sao_Paulo", "America/Toronto",
        "Asia/Dubai", "Asia/Hong_Kong", "Asia/Jakarta", "Asia/Karachi", "Asia/Kolkata", "Asia/Shanghai",
        "Asia/Singapore", "Asia/Tokyo",
        "Australia/Sydney",
        "Europe/Amsterdam", "Europe/Berlin", "Europe/Istanbul", "Europe/London", "Europe/Madrid", "Europe/Moscow",
        "Europe/Paris", "Europe/Rome",
        "Pacific/Auckland"
    ];

    public static readonly IniSetting[] All =
    [
        new("upload_max_filesize", "Uploads", "Max upload file size", IniValueKind.Size, "2M", "Largest single uploaded file. Keep it at or below post_max_size.", Sizes),
        new("post_max_size", "Uploads", "Max POST size", IniValueKind.Size, "8M", "Largest request body, including all uploaded files.", Sizes),
        new("max_file_uploads", "Uploads", "Max files per request", IniValueKind.Int, "20", "", ["5", "10", "20", "50", "100"]),
        new("file_uploads", "Uploads", "Allow file uploads", IniValueKind.Bool, "On"),

        new("memory_limit", "Resources", "Memory limit", IniValueKind.Size, "128M", "-1 means unlimited.", MemorySizes),
        new("max_execution_time", "Resources", "Max execution time (s)", IniValueKind.Int, "30", "0 means unlimited. The CLI is always unlimited.", ["30", "60", "120", "300", "600", "0"]),
        new("max_input_time", "Resources", "Max input time (s)", IniValueKind.Int, "60", "-1 uses max_execution_time.", ["60", "120", "300", "-1"]),
        new("max_input_vars", "Resources", "Max input vars", IniValueKind.Int, "1000", "Raise for very large forms.", ["1000", "3000", "5000", "10000", "20000"]),
        new("realpath_cache_size", "Resources", "Realpath cache size", IniValueKind.Size, "4096K", "Larger values speed up big frameworks on Windows.", ["4096K", "8M", "16M", "32M"]),
        new("realpath_cache_ttl", "Resources", "Realpath cache TTL (s)", IniValueKind.Int, "120", "", ["120", "300", "600", "3600"]),

        new("display_errors", "Errors", "Display errors", IniValueKind.Bool, "Off", "On for development, Off for production."),
        new("error_reporting", "Errors", "Error reporting", IniValueKind.Text, "E_ALL", "Which error levels are reported.",
            ["E_ALL", "E_ALL & ~E_DEPRECATED & ~E_STRICT", "E_ALL & ~E_NOTICE", "E_ALL & ~E_DEPRECATED", "E_ERROR | E_WARNING | E_PARSE", "0"]),
        new("log_errors", "Errors", "Log errors", IniValueKind.Bool, "Off"),
        new("error_log", "Errors", "Error log file", IniValueKind.Text, "(stderr)", "Full path of the log file. Use Custom to enter one.", []),

        new("date.timezone", "Locale", "Default timezone", IniValueKind.Text, "UTC", "IANA name, for example Africa/Lagos.", TimeZones),
        new("default_charset", "Locale", "Default charset", IniValueKind.Text, "UTF-8", "", ["UTF-8", "ISO-8859-1", "Windows-1252"]),

        new("opcache.enable", "OPcache", "OPcache enabled (web)", IniValueKind.Bool, "On", "Needs OPcache loaded (see the checkbox above)."),
        new("opcache.enable_cli", "OPcache", "OPcache enabled (CLI)", IniValueKind.Bool, "Off"),
        new("opcache.memory_consumption", "OPcache", "OPcache memory (MB)", IniValueKind.Int, "128", "", ["64", "128", "192", "256", "512"]),
        new("opcache.max_accelerated_files", "OPcache", "Max cached files", IniValueKind.Int, "10000", "Laravel apps with vendor code usually need 10000 or more.", ["4000", "10000", "20000", "50000"]),
        new("opcache.validate_timestamps", "OPcache", "Check files for changes", IniValueKind.Bool, "On", "Turn Off in production for speed (restart PHP after deploys)."),
        new("opcache.revalidate_freq", "OPcache", "Recheck interval (s)", IniValueKind.Int, "2", "Only used when files are checked for changes.", ["0", "2", "10", "60", "300"]),
        new("opcache.jit", "OPcache", "JIT mode", IniValueKind.Text, "disable", "tracing is the usual choice when enabling the JIT.", ["disable", "tracing", "function"], AllowCustom: false),
        new("opcache.jit_buffer_size", "OPcache", "JIT buffer size", IniValueKind.Size, "0", "0 turns the JIT off.", ["0", "64M", "100M", "128M", "256M"]),

        new("session.gc_maxlifetime", "Sessions", "Session lifetime (s)", IniValueKind.Int, "1440", "", ["1440", "3600", "7200", "14400", "86400"]),
        new("session.cookie_secure", "Sessions", "Secure session cookie", IniValueKind.Bool, "Off", "On when the site is served over HTTPS."),

        new("allow_url_fopen", "Other", "Allow URL fopen", IniValueKind.Bool, "On"),
        new("short_open_tag", "Other", "Short open tags", IniValueKind.Bool, "Off"),
        new("expose_php", "Other", "Expose PHP version header", IniValueKind.Bool, "On"),
        new("zend.assertions", "Other", "Assertions", IniValueKind.Int, "-1", "-1 off (production), 1 on (development).", ["-1", "0", "1"], AllowCustom: false)
    ];

    public static IniSetting? Find(string key) =>
        All.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns an error message, or null when the value is acceptable for the setting.</summary>
    public static string? Validate(IniSetting setting, string? value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0)
            return $"{setting.Label}: enter a value.";

        switch (setting.Kind)
        {
            case IniValueKind.Size:
                if (!Regex.IsMatch(value, @"^(-1|\d+[KkMmGg]?)$"))
                    return $"{setting.Label}: use a number with an optional K, M or G (for example 128M), or -1.";
                break;
            case IniValueKind.Int:
                if (!Regex.IsMatch(value, @"^-?\d+$"))
                    return $"{setting.Label}: use a whole number.";
                break;
            case IniValueKind.Bool:
                if (!Regex.IsMatch(value, @"^(On|Off)$", RegexOptions.IgnoreCase))
                    return $"{setting.Label}: use On or Off.";
                break;
            default:
                if (value.Contains('\n') || value.Contains('\r'))
                    return $"{setting.Label}: must be a single line.";
                break;
        }

        return null;
    }

    /// <summary>Marker returned by <see cref="ToBytes"/> for -1 (unlimited).</summary>
    public const long Unlimited = long.MaxValue;

    /// <summary>Parses a php size (128M, 2G, 512K, 1024, -1) to bytes; null if it isn't one.</summary>
    public static long? ToBytes(string? value)
    {
        var m = Regex.Match((value ?? "").Trim(), @"^(-1|(?<n>\d+)(?<u>[KkMmGg])?)$");
        if (!m.Success)
            return null;
        if (m.Value == "-1")
            return Unlimited;

        var n = long.Parse(m.Groups["n"].Value);
        return char.ToUpperInvariant(m.Groups["u"].Value.FirstOrDefault()) switch
        {
            'K' => n * 1024,
            'M' => n * 1024 * 1024,
            'G' => n * 1024 * 1024 * 1024,
            _ => n
        };
    }

    /// <summary>Directive names are letters, digits, underscore and dot (for example session.save_path).</summary>
    public static bool IsValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Regex.IsMatch(key, @"^[A-Za-z_][A-Za-z0-9_.]*$");

    /// <summary>Size/int numbers are written as typed; On/Off is normalised to the casing php.ini uses.</summary>
    public static string Normalize(IniSetting setting, string value)
    {
        value = value.Trim();
        if (setting.Kind == IniValueKind.Bool)
            return value.Equals("on", StringComparison.OrdinalIgnoreCase) ? "On" : "Off";
        if (setting.Kind == IniValueKind.Size && value.Length > 0 && char.IsLetter(value[^1]))
            return value[..^1] + char.ToUpperInvariant(value[^1]);
        return value;
    }
}
