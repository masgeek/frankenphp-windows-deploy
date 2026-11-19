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
            content = PhpIniEditor.SetExtensionState(content, name, enabled: true, zend: false);

        return content;
    }
}
