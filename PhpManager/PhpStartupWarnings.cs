using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>Extracts "Unable to load dynamic library" style warnings from php output so failures can be explained.</summary>
public static class PhpStartupWarnings
{
    private static readonly Regex LoadFailure = new(
        @"(?im)^(?:PHP )?Warning:\s+PHP Startup:\s+Unable to load dynamic library '(?:php_)?(?<name>\w+?)(?:\.dll)?'(?<reason>.*)$",
        RegexOptions.Compiled);

    /// <summary>Extension name (lowercase, no php_ / .dll) to the reason PHP gave.</summary>
    public static Dictionary<string, string> ParseLoadFailures(string? output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in LoadFailure.Matches(output ?? ""))
        {
            var reason = m.Groups["reason"].Value.Trim();
            // "(tried: C:\PHP\ext\php_x.dll (The specified module could not be found), ...)" -> keep the OS message.
            var os = Regex.Match(reason, @"\(([^()]*(?:could not be found|not a valid|Access is denied|specified procedure)[^()]*)\)");
            result[m.Groups["name"].Value] = os.Success ? os.Groups[1].Value : reason.Trim('(', ')', ' ', '.');
        }

        return result;
    }
}
