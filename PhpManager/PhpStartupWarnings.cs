using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>Turns PHP's startup warnings about extensions into plain explanations so a failed load can be understood.</summary>
public static class PhpStartupWarnings
{
    private static readonly Regex LoadFailure = new(
        @"(?im)^(?:PHP )?Warning:\s+PHP Startup:\s+Unable to load dynamic library '(?:php_)?(?<name>\w+?)(?:\.dll)?'(?<reason>.*)$",
        RegexOptions.Compiled);

    /// <summary>Module "x" is already loaded: the extension is listed on more than one php.ini line.</summary>
    private static readonly Regex AlreadyLoaded = new(
        @"(?im)^(?:PHP )?Warning:\s+Module [""'](?<name>\w+)[""'] is already loaded",
        RegexOptions.Compiled);

    /// <summary>The DLL was built for a different PHP version (module API) than the one running.</summary>
    private static readonly Regex ApiMismatch = new(
        @"(?is)(?:PHP )?Warning:\s+PHP Startup:\s+(?<name>\w+):\s+Unable to initialize module\s+Module compiled with module API=(?<mod>\d+)\s+PHP\s+compiled with module API=(?<php>\d+)",
        RegexOptions.Compiled);

    /// <summary>The DLL was built with a different thread-safety or compiler (build ID) than the running PHP.</summary>
    private static readonly Regex BuildMismatch = new(
        @"(?is)(?:PHP )?Warning:\s+PHP Startup:\s+(?<name>\w+):\s+Unable to initialize module\s+Module compiled with build ID=(?<mod>[^\r\n]+?)\s+PHP\s+compiled with build ID=(?<php>[^\r\n]+)",
        RegexOptions.Compiled);

    /// <summary>Extension name (lowercase, no php_ / .dll) to the reason PHP gave.</summary>
    public static Dictionary<string, string> ParseLoadFailures(string? output)
    {
        output ??= "";
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in LoadFailure.Matches(output))
        {
            var reason = m.Groups["reason"].Value.Trim();
            // "(tried: C:\PHP\ext\php_x.dll (The specified module could not be found), ...)" -> keep the OS message.
            var os = Regex.Match(reason, @"\(([^()]*(?:could not be found|not a valid|Access is denied|specified procedure)[^()]*)\)");
            result[m.Groups["name"].Value] = os.Success ? os.Groups[1].Value : reason.Trim('(', ')', ' ', '.');
        }

        foreach (Match m in AlreadyLoaded.Matches(output))
            result[m.Groups["name"].Value] = "listed more than once in php.ini (already loaded); remove the duplicate line";

        foreach (Match m in ApiMismatch.Matches(output))
            result[m.Groups["name"].Value] =
                $"built for a different PHP version (module API {m.Groups["mod"].Value}, this PHP uses {m.Groups["php"].Value}); install the driver build that matches this PHP";

        foreach (Match m in BuildMismatch.Matches(output))
            result[m.Groups["name"].Value] =
                $"built for a different PHP build ({m.Groups["mod"].Value.Trim()}), but this PHP is {m.Groups["php"].Value.Trim()}; thread-safe (TS) and non-thread-safe (NTS) builds are not interchangeable";

        return result;
    }
}
