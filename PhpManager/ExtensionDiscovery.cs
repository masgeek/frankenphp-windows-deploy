using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>
/// Builds the Extensions list: php.ini extension= and zend_extension= lines plus DLLs present in the ext folder
/// (listed disabled when they have no line). Extensions compiled into PHP are deliberately not listed: they are
/// always on and never need enabling.
/// </summary>
public static class ExtensionDiscovery
{
    /// <summary>DLLs that load with zend_extension, so enabling one must write zend_extension=.</summary>
    private static readonly HashSet<string> ZendNames = new(StringComparer.OrdinalIgnoreCase) { "opcache", "xdebug" };

    /// <summary>DLLs that are test-only and never offered.</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase) { "zend_test" };

    private const string Name = @"""?(?:[^""\r\n;]*[\\/])?(?:php_)?(?<name>\w+)(?:\.dll)?""?";

    /// <summary>Extensions whose desired state differs from what php.ini currently says (built-ins are never changed).</summary>
    public static List<ExtensionInfo> Changes(string iniContent, IEnumerable<ExtensionInfo> desired)
    {
        var current = Build(iniContent, []).ToDictionary(e => e.Name, e => e.Enabled, StringComparer.OrdinalIgnoreCase);
        return desired.Where(e => current.GetValueOrDefault(e.Name) != e.Enabled).ToList();
    }

    public static List<ExtensionInfo> Build(string iniContent, IEnumerable<string> dllNames)
    {
        var byName = new Dictionary<string, ExtensionInfo>(StringComparer.OrdinalIgnoreCase);

        void AddIni(string key, ExtensionKind kind)
        {
            var pattern = $@"(?m)^[ \t]*;?[ \t]*{key}[ \t]*=[ \t]*{Name}[ \t]*\r?$";
            foreach (Match m in Regex.Matches(iniContent, pattern))
            {
                var name = m.Groups["name"].Value;
                var enabled = !m.Value.TrimStart().StartsWith(';');

                // A name can appear both commented (template) and active (added later): active wins.
                if (!byName.TryGetValue(name, out var existing))
                    byName[name] = new ExtensionInfo { Name = name, Enabled = enabled, Kind = kind };
                else if (enabled)
                    existing.Enabled = true;
            }
        }

        AddIni("zend_extension", ExtensionKind.Zend);
        AddIni("extension", ExtensionKind.Dll);

        // DLLs with no php.ini line at all (installed later, or never in the template) are listed as disabled.
        foreach (var dll in dllNames)
        {
            var name = Regex.Replace(dll, @"^php_|\.dll$", "", RegexOptions.IgnoreCase);
            if (name.Length == 0 || Hidden.Contains(name) || byName.ContainsKey(name))
                continue;

            byName[name] = new ExtensionInfo
            {
                Name = name,
                Enabled = false,
                Kind = ZendNames.Contains(name) ? ExtensionKind.Zend : ExtensionKind.Dll
            };
        }

        return byName.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
