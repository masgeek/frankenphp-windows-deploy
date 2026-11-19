using System.Text.RegularExpressions;

namespace PhpManager;

public enum SupportStatus
{
    /// <summary>Still receiving bug fixes.</summary>
    Active,
    /// <summary>Security fixes only; plan an upgrade.</summary>
    SecurityOnly,
    /// <summary>No fixes at all.</summary>
    EndOfLife,
    Unknown
}

/// <summary>
/// PHP release support windows (php.net/supported-versions). The table is a snapshot: update it when new
/// minor versions are released.
/// </summary>
public static class PhpSupport
{
    private static readonly Dictionary<string, (DateOnly ActiveUntil, DateOnly SecurityUntil)> Table = new()
    {
        ["8.5"] = (new(2027, 12, 31), new(2029, 12, 31)),
        ["8.4"] = (new(2026, 12, 31), new(2028, 12, 31)),
        ["8.3"] = (new(2025, 12, 31), new(2027, 12, 31)),
        ["8.2"] = (new(2024, 12, 31), new(2026, 12, 31)),
        ["8.1"] = (new(2023, 11, 25), new(2025, 12, 31)),
        ["8.0"] = (new(2022, 11, 26), new(2023, 11, 26)),
        ["7.4"] = (new(2021, 11, 28), new(2022, 11, 28)),
        ["7.3"] = (new(2020, 12, 6), new(2021, 12, 6)),
        ["7.2"] = (new(2019, 11, 30), new(2020, 11, 30)),
        ["7.1"] = (new(2018, 12, 1), new(2019, 12, 1)),
        ["7.0"] = (new(2018, 1, 4), new(2018, 12, 3)),
        ["5.6"] = (new(2017, 1, 19), new(2018, 12, 31))
    };

    public static string MajorMinor(string version)
    {
        var m = Regex.Match(version, @"^(\d+)\.(\d+)");
        return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : "";
    }

    public static SupportStatus StatusOf(string version, DateOnly today)
    {
        if (!Table.TryGetValue(MajorMinor(version), out var window))
            return SupportStatus.Unknown;

        if (today <= window.ActiveUntil)
            return SupportStatus.Active;
        return today <= window.SecurityUntil ? SupportStatus.SecurityOnly : SupportStatus.EndOfLife;
    }

    public static DateOnly? SecurityEnd(string version) =>
        Table.TryGetValue(MajorMinor(version), out var w) ? w.SecurityUntil : null;
}
