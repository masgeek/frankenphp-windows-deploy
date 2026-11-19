using System.Text;
using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>Pure php.ini text edits (no file or process access).</summary>
public static class PhpIniEditor
{
    /// <summary>Comments out the <c>extension=name</c> and <c>zend_extension=name</c> lines for each name. Other lines are untouched.</summary>
    public static string DisableExtensions(string iniContent, IEnumerable<string> names)
    {
        var content = iniContent;
        foreach (var name in names)
        {
            content = SetExtensionState(content, name, enabled: false, zend: false, addIfMissing: false);
            content = SetExtensionState(content, name, enabled: false, zend: true, addIfMissing: false);
        }

        return content;
    }

    /// <summary>One extension= or zend_extension= line, active or commented, with optional php_ prefix, .dll suffix, quotes and path.</summary>
    private const string ExtLine =
        @"(?m)^(?<indent>[ \t]*)(?<semi>;?)[ \t]*(?<key>zend_extension|extension)[ \t]*=[ \t]*""?(?:[^""\r\n;]*[\\/])?(?:(?i:php_))?(?<name>\w+)(?:(?i:\.dll))?""?[ \t]*(?<cr>\r?)$";

    /// <summary>
    /// Enables or disables <c>extension=name</c> (or <c>zend_extension=name</c> when <paramref name="zend"/>) and
    /// guarantees the name appears on exactly one line afterwards: the first active line is kept (else the first
    /// commented one) and any other lines for it are removed, so php.ini never ends up with duplicates.
    /// </summary>
    public static string SetExtensionState(string content, string name, bool enabled, bool zend, bool addIfMissing = true)
    {
        var key = zend ? "zend_extension" : "extension";
        var matches = Regex.Matches(content, ExtLine).Cast<Match>()
            .Where(m => m.Groups["key"].Value == key && m.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return enabled && addIfMissing ? InsertDirective(content, $"{key}={name}") : content;

        var keeper = matches.FirstOrDefault(m => m.Groups["semi"].Length == 0) ?? matches[0];
        var result = new StringBuilder();
        var pos = 0;
        foreach (var m in matches)
        {
            result.Append(content, pos, m.Index - pos);
            pos = m.Index + m.Length;

            if (m == keeper)
            {
                var body = m.Value.TrimEnd('\r').TrimStart(' ', '\t', ';').TrimStart();
                result.Append(m.Groups["indent"].Value).Append(enabled ? "" : ";").Append(body).Append(m.Groups["cr"].Value);
            }
            else if (pos < content.Length && content[pos] == '\n')
            {
                pos++; // drop the whole duplicate line, including its line break
            }
        }

        result.Append(content, pos, content.Length - pos);
        return result.ToString();
    }

    /// <summary>Names (as key=name) that appear on more than one line, in any state.</summary>
    public static List<string> FindDuplicateExtensions(string content) =>
        Regex.Matches(content, ExtLine).Cast<Match>()
            .GroupBy(m => (m.Groups["key"].Value, m.Groups["name"].Value.ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Item1}={g.Key.Item2}")
            .ToList();

    /// <summary>Collapses every duplicated extension/zend_extension line to one (the active one if any), leaving everything else alone.</summary>
    public static string DeduplicateExtensions(string content)
    {
        var remove = new List<Match>();
        foreach (var g in Regex.Matches(content, ExtLine).Cast<Match>()
                     .GroupBy(m => (m.Groups["key"].Value, m.Groups["name"].Value.ToLowerInvariant()))
                     .Where(g => g.Count() > 1))
        {
            var keeper = g.FirstOrDefault(m => m.Groups["semi"].Length == 0) ?? g.First();
            remove.AddRange(g.Where(m => m != keeper));
        }

        if (remove.Count == 0)
            return content;

        var result = new StringBuilder();
        var pos = 0;
        foreach (var m in remove.OrderBy(m => m.Index))
        {
            result.Append(content, pos, m.Index - pos);
            pos = m.Index + m.Length;
            if (pos < content.Length && content[pos] == '\n')
                pos++;
        }

        result.Append(content, pos, content.Length - pos);
        return result.ToString();
    }

    /// <summary>The effective (last active) value of a directive, or null when it isn't set. Inline comments and quotes are stripped.</summary>
    public static string? GetDirective(string content, string key)
    {
        var matches = Regex.Matches(content, $@"(?m)^[ \t]*{Regex.Escape(key)}[ \t]*=[ \t]*(.*?)[ \t]*\r?$");
        if (matches.Count == 0)
            return null;

        var value = matches[^1].Groups[1].Value;
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            return end > 0 ? value[1..end] : value.Trim('"');
        }

        var comment = Regex.Match(value, @"[ \t];.*$");
        return (comment.Success ? value[..comment.Index] : value).Trim();
    }

    /// <summary>
    /// Sets a directive. Replaces the last active line; otherwise uncomments the first commented
    /// <c>;key = ...</c> line; otherwise adds the line (before the first non-[PHP] section).
    /// </summary>
    public static string SetDirective(string content, string key, string value)
    {
        var k = Regex.Escape(key);
        var active = Regex.Matches(content, $@"(?m)^([ \t]*{k}[ \t]*=[ \t]*)(.*?)([ \t]*\r?)$");
        if (active.Count > 0)
        {
            var last = active[^1];
            return content[..last.Index] + last.Groups[1].Value + value + last.Groups[3].Value + content[(last.Index + last.Length)..];
        }

        var commented = Regex.Match(content, $@"(?m)^[ \t]*;[ \t]*{k}[ \t]*=[ \t]*(.*?)([ \t]*\r?)$");
        if (commented.Success)
            return content[..commented.Index] + $"{key} = {value}" + commented.Groups[2].Value + content[(commented.Index + commented.Length)..];

        return InsertDirective(content, $"{key} = {value}");
    }

    private static string InsertDirective(string content, string line)
    {
        var eol = content.Contains("\r\n") ? "\r\n" : "\n";
        var nextSection = Regex.Match(content, @"(?m)^\[(?!PHP\])");
        return nextSection.Success
            ? content.Insert(nextSection.Index, line + eol)
            : content.TrimEnd('\r', '\n') + eol + line + eol;
    }
}
