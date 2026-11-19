using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>Pure php.ini text edits (no file or process access).</summary>
public static class PhpIniEditor
{
    /// <summary>Comments out active <c>extension=name</c> lines (also php_name.dll forms). Other lines are untouched.</summary>
    public static string DisableExtensions(string iniContent, IEnumerable<string> names)
    {
        var content = iniContent;
        foreach (var name in names)
            content = SetExtensionState(content, name, enabled: false, zend: false, addIfMissing: false);

        return content;
    }

    /// <summary>
    /// Enables or disables <c>extension=name</c> (or <c>zend_extension=name</c> when <paramref name="zend"/>).
    /// Enabling uncomments the first matching line (or appends one); disabling comments out every active match.
    /// </summary>
    public static string SetExtensionState(string content, string name, bool enabled, bool zend, bool addIfMissing = true)
    {
        var key = zend ? "zend_extension" : "extension";
        var pattern = $@"(?m)^([ \t]*);?[ \t]*{key}[ \t]*=[ \t]*""?(?:[^""\r\n;]*[\\/])?(?:php_)?{Regex.Escape(name)}(?:\.dll)?""?[ \t]*\r?$";

        var matches = Regex.Matches(content, pattern);
        if (matches.Count == 0)
            return enabled && addIfMissing ? InsertDirective(content, $"{key}={name}") : content;

        var seenActive = false;
        var index = 0;
        return Regex.Replace(content, pattern, m =>
        {
            var cr = m.Value.EndsWith('\r') ? "\r" : "";
            var body = m.Value.TrimEnd('\r').TrimStart(' ', '\t', ';').TrimStart();
            var wasActive = !m.Value.TrimStart().StartsWith(';');
            index++;

            if (enabled)
            {
                // keep exactly one active line: the first match, or the first one that already was active
                if (wasActive) { seenActive = true; return m.Value; }
                var isFirst = index == 1;
                if (isFirst && !matches.Cast<Match>().Any(x => !x.Value.TrimStart().StartsWith(';')))
                    return body + cr;
                return m.Value;
            }

            return wasActive ? ";" + body + cr : m.Value;
        });
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
