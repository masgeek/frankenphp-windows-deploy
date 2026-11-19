namespace PhpManager;

/// <summary>xdebug.mode is a comma-separated set (for example "debug,develop"), or "off" when empty.</summary>
public static class XdebugModes
{
    public static readonly string[] All = ["develop", "debug", "profile", "trace", "coverage", "gcstats"];

    public static HashSet<string> Parse(string? value) =>
        (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.ToLowerInvariant())
            .Where(v => All.Contains(v))
            .ToHashSet();

    /// <summary>Canonical-order, comma-separated; "off" when nothing is selected.</summary>
    public static string Format(IEnumerable<string> selected)
    {
        var set = selected.Select(v => v.ToLowerInvariant()).ToHashSet();
        var ordered = All.Where(set.Contains).ToList();
        return ordered.Count == 0 ? "off" : string.Join(",", ordered);
    }
}
