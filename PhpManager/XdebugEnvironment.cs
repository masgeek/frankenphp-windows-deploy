using System.Text.RegularExpressions;

namespace PhpManager;

public class XdebugEnvironment
{
    public string PhpVersion { get; set; } = "";
    public string PhpMajorMinor { get; set; } = "";
    public bool ThreadSafe { get; set; }
    public string Architecture { get; set; } = "x86";
    public bool IsWindows { get; set; } = true;
    public bool DebugBuild { get; set; }
    public int WinCompiler { get; set; }
    public string ConfigFile { get; set; } = "";
    public string ExtensionDir { get; set; } = "";
    public string Sapi { get; set; } = "";
    public bool XdebugLoaded { get; set; }
    public string LoadedXdebugVersion { get; set; } = "";

    public string ThreadSafety => ThreadSafe ? "ts" : "nts";
    public string CompilerPrefix => WinCompiler >= 16 ? "vs" : "vc";
    public string Compiler => $"{CompilerPrefix}{WinCompiler}";
    public string ExtensionSuffix => Architecture == "x64" ? "x86_64" : "x86";

    public string Summary =>
        $"PHP {PhpVersion} | {ThreadSafety.ToUpperInvariant()} | {Architecture} | {Compiler} | " +
        $"{(DebugBuild ? "debug" : "release")} build";
}

public record XdebugVerdict(bool Supported, string Message, string RecommendedVersion)
{
    public static XdebugVerdict Ok(string version) => new(true, $"Xdebug {version} is available for this build.", version);
    public static XdebugVerdict Fail(string message) => new(false, message, "");
}

public class XdebugCompat
{
    public Dictionary<string, string> Latest { get; set; } = new();
    public Dictionary<string, int> Compiler { get; set; } = new();
    public List<UnsupportedRange> Unsupported { get; set; } = new();

    public class UnsupportedRange
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public static XdebugCompat Current => AppConfig.Get<XdebugCompat>(CompatKey) ?? Defaults;

    public static void Save(XdebugCompat value) => AppConfig.Set(CompatKey, value);

    private const string CompatKey = "xdebug.compatibility";

    private static readonly XdebugCompat Defaults = new()
    {
        Latest = new Dictionary<string, string>
        {
            ["7.1"] = "2.9.8",
            ["7.2"] = "3.1.6",
            ["7.3"] = "3.1.6",
            ["7.4"] = "3.1.6",
            ["8.0"] = "3.5.3",
            ["8.1"] = "3.5.3",
            ["8.2"] = "3.5.3",
            ["8.3"] = "3.5.3",
            ["8.4"] = "3.5.3",
            ["8.5"] = "3.5.3"
        },
        Compiler = new Dictionary<string, int>
        {
            ["7.0"] = 14, ["7.1"] = 15,
            ["7.2"] = 15, ["7.3"] = 15, ["7.4"] = 15,
            ["8.0"] = 16, ["8.1"] = 16, ["8.2"] = 16, ["8.3"] = 16,
            ["8.4"] = 17, ["8.5"] = 17
        },
        Unsupported = new List<XdebugCompat.UnsupportedRange>
        {
            new() { From = "7.4.0", To = "7.4.20", Reason = "due to missing exported symbols in zlib" },
            new() { From = "8.0.0", To = "8.0.7", Reason = "due to missing exported symbols in zlib" }
        }
    };
}

public static class XdebugCompatibility
{
    public static XdebugVerdict Evaluate(XdebugEnvironment env)
    {
        var compat = XdebugCompat.Current;

        if (string.IsNullOrEmpty(env.PhpVersion))
            return XdebugVerdict.Fail("Could not find any useful information.");

        if (!Version.TryParse(env.PhpVersion.Split('-')[0], out var phpVersion))
            return XdebugVerdict.Fail($"PHP version '{env.PhpVersion}' could not be parsed.");

        foreach (var range in compat.Unsupported)
        {
            if (!Version.TryParse(range.From, out var from) || !Version.TryParse(range.To, out var to))
                continue;

            if (phpVersion >= from && phpVersion < to)
                return XdebugVerdict.Fail($"PHP {env.PhpVersion} is not supported on Windows {range.Reason}, upgrade to at least {range.To}.");
        }

        if (!compat.Latest.TryGetValue(env.PhpMajorMinor, out var latest) || string.IsNullOrEmpty(latest))
        {
            if (env.PhpMajorMinor.StartsWith("7.0"))
                return XdebugVerdict.Fail("Windows binaries for PHP 7.0 are not available, because this PHP version is no longer supported by the PHP project.");
            return XdebugVerdict.Fail($"PHP version {env.PhpMajorMinor} is not supported.");
        }

        if (env.DebugBuild)
            return XdebugVerdict.Fail("Debug builds are not supported on Windows.");

        if (env.WinCompiler is 6 or 8 or 9)
            return XdebugVerdict.Fail($"The compiler (MS VC{env.WinCompiler}) that this PHP was built with is no longer supported. Please upgrade to a version built with MS VC15 or MS VS16/VS17.");

        if (compat.Compiler.TryGetValue(env.PhpMajorMinor, out var expected) && env.WinCompiler != expected)
            return XdebugVerdict.Fail($"The compiler (MS {env.Compiler}) that this PHP {env.PhpMajorMinor} was built with is not supported. Expected VS{expected}.");

        if (env.Architecture != "x64")
            return XdebugVerdict.Fail("32-bit builds of Xdebug on Windows are no longer available. Please use a 64-bit version of PHP.");

        return XdebugVerdict.Ok(latest);
    }

    public static string BuildZipName(XdebugEnvironment env, string xdebugVersion) =>
        $"php_xdebug-{xdebugVersion}-{env.PhpMajorMinor}-{env.ThreadSafety}-{env.Compiler}-{env.Architecture}";

    public static string BuildExpectedDllName(XdebugEnvironment env, string xdebugVersion)
    {
        if (Version.TryParse(xdebugVersion, out var v) && v >= new Version(3, 4, 1))
        {
            return $"php_xdebug-{xdebugVersion}-{env.PhpMajorMinor.Replace(".", "")}" +
                   $"{(env.ThreadSafe ? "-ts" : "-nts")}-{env.Compiler}-{env.ExtensionSuffix}.dll";
        }

        return $"php_xdebug-{xdebugVersion}-{env.PhpMajorMinor}-{env.Compiler}" +
               $"{(env.ThreadSafe ? "" : "-nts")}-{env.Architecture}.dll";
    }

    /// <summary>Parses raw phpinfo()/php -i output the same way the official wizard does.</summary>
    public static XdebugEnvironment Parse(string data)
    {
        var env = new XdebugEnvironment();

        if (string.IsNullOrWhiteSpace(data))
            return env;

        var text = Regex.Replace(data, "<[^>]+>", " ");
        text = text.Replace("&nbsp;", " ");

        var versionMatch = Regex.Match(text, @"PHP Version[^0-9]*([0-9][0-9a-zA-Z.\-]*)");
        if (versionMatch.Success)
        {
            env.PhpVersion = versionMatch.Groups[1].Value.Trim();
            if (env.PhpVersion.Length >= 3)
                env.PhpMajorMinor = env.PhpVersion[..3];
        }

        var sapiMatch = Regex.Match(text, @"Server API\s*(?:=>|\t)\s*(.+)");
        if (sapiMatch.Success)
            env.Sapi = sapiMatch.Groups[1].Value.Trim();

        var tsMatch = Regex.Match(text, @"Thread Safety\s*(?:=>|\t)\s*(enabled|disabled)");
        if (tsMatch.Success)
            env.ThreadSafe = tsMatch.Groups[1].Value == "enabled";

        var debugMatch = Regex.Match(text, @"Debug Build\s*(?:=>|\t)\s*(yes|no)");
        if (debugMatch.Success)
            env.DebugBuild = debugMatch.Groups[1].Value == "yes";

        if (Regex.IsMatch(text, @"System\s*(?:=>|\t)\s*Windows"))
            env.IsWindows = true;

        var archMatch = Regex.Match(text, @"Architecture\s*(?:=>|\t)\s*(x[0-9]+)");
        if (archMatch.Success)
            env.Architecture = archMatch.Groups[1].Value;

        var configMatch = Regex.Match(text, @"Loaded Configuration File\s*(?:=>|\t)\s*(.+)");
        if (configMatch.Success)
        {
            var value = configMatch.Groups[1].Value.Trim();
            env.ConfigFile = value == "(none)" ? "" : value;
        }

        var extDirMatch = Regex.Match(text, @"extension_dir\s*(?:=>|\t)\s*(.+?)(?:\s*=>|$)");
        if (extDirMatch.Success)
            env.ExtensionDir = extDirMatch.Groups[1].Value.Trim().TrimEnd('\\');

        var buildMatch = Regex.Match(text, @"PHP Extension Build\s*(?:=>|\t)\s*(API[^,]*(?:,[^\r\n]*)?)");
        if (buildMatch.Success)
        {
            foreach (var part in buildMatch.Groups[1].Value.Split(','))
            {
                var token = part.Trim();
                if (Regex.IsMatch(token, @"^(TS|NTS)$", RegexOptions.IgnoreCase))
                    env.ThreadSafe = token.Equals("TS", StringComparison.OrdinalIgnoreCase);
                else if (token.Equals("debug", StringComparison.OrdinalIgnoreCase))
                    env.DebugBuild = true;
                else if (Regex.IsMatch(token, @"^(VC|VS)(\d+)$", RegexOptions.IgnoreCase))
                    env.WinCompiler = int.Parse(Regex.Match(token, @"(\d+)$").Groups[1].Value);
            }
        }

        var loadedMatch = Regex.Match(text, @"with\s+Xdebug\s+v([0-9.RCrcdevalphabeta-]+)");
        if (loadedMatch.Success)
        {
            env.XdebugLoaded = true;
            env.LoadedXdebugVersion = loadedMatch.Groups[1].Value;
        }

        if (Regex.IsMatch(text, @"xdebug support", RegexOptions.IgnoreCase))
            env.XdebugLoaded = true;

        return env;
    }
}