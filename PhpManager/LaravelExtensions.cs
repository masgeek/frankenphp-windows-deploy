using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>
/// The DLL-based extensions a Laravel app needs on Windows. Extensions compiled into PHP (ctype, dom, filter, hash,
/// json, pcre, session, tokenizer, xml, bcmath, ...) need no php.ini line and are not listed here.
/// </summary>
public static class LaravelExtensions
{
    public static readonly string[] Names =
    [
        "curl", "exif", "fileinfo", "gd", "intl", "mbstring", "mysqli",
        "openssl", "pdo_mysql", "pdo_sqlite", "sqlite3", "zip"
    ];

    public static bool IsLaravel(string name) =>
        Names.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Uncomments (or appends) <c>extension=name</c> for each of <paramref name="names"/>. Already-enabled lines are
    /// left untouched and no duplicates are added.
    /// </summary>
    public static string Enable(string iniContent, IEnumerable<string> names)
    {
        var content = iniContent;
        foreach (var name in names)
        {
            var pattern = $@"(?m)^[ \t]*;?[ \t]*extension[ \t]*=[ \t]*(?:php_)?{Regex.Escape(name)}(?:\.dll)?[ \t]*\r?$";
            var match = Regex.Match(content, pattern);
            if (match.Success)
            {
                if (match.Value.TrimStart().StartsWith(';'))
                    content = content.Remove(match.Index, match.Length)
                        .Insert(match.Index, $"extension={name}" + (match.Value.EndsWith('\r') ? "\r" : ""));
                continue;
            }

            var nextSection = Regex.Match(content, @"(?m)^\[(?!PHP\])");
            var directive = $"extension={name}\n";
            content = nextSection.Success
                ? content.Insert(nextSection.Index, directive)
                : content.TrimEnd() + "\n" + directive;
        }

        return content;
    }
}
