namespace PhpManager;

/// <summary>Puts each PHP extension into a functional group so the Extensions list is easy to scan.</summary>
public static class ExtensionCatalog
{
    public const string Debugging = "Debugging";
    public const string Database = "Database";
    public const string Caching = "Caching & performance";
    public const string Web = "Web & networking";
    public const string Security = "Security & crypto";
    public const string Text = "Text & internationalisation";
    public const string Xml = "XML & data formats";
    public const string Files = "Images, files & archives";
    public const string Math = "Math";
    public const string Other = "Other";

    /// <summary>Display order of the groups.</summary>
    public static readonly string[] Order =
        [Database, Caching, Web, Security, Text, Xml, Files, Math, Debugging, Other];

    private static readonly Dictionary<string, string> Groups = Build();

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string group, params string[] names)
        {
            foreach (var n in names)
                map[n] = group;
        }

        Add(Database, "mysqli", "mysqlnd", "pdo", "pdo_mysql", "pdo_sqlite", "sqlite3", "pdo_pgsql", "pgsql",
            "pdo_sqlsrv", "sqlsrv", "pdo_oci", "oci8", "pdo_odbc", "odbc", "pdo_firebird", "dba", "mongodb");
        Add(Caching, "opcache", "redis", "apcu", "memcached", "memcache");
        Add(Web, "curl", "sockets", "ftp", "soap", "imap", "ldap", "snmp", "http", "session", "mailparse");
        Add(Security, "openssl", "sodium", "hash", "password");
        Add(Text, "mbstring", "intl", "iconv", "gettext", "ctype", "tokenizer", "pcre", "json", "filter", "enchant", "tidy");
        Add(Xml, "dom", "xml", "xmlreader", "xmlwriter", "simplexml", "xsl", "libxml");
        Add(Files, "gd", "exif", "fileinfo", "imagick", "zip", "zlib", "bz2", "phar", "shmop");
        Add(Math, "bcmath", "gmp");
        Add(Debugging, "xdebug", "zend_test", "pcov");
        return map;
    }

    /// <summary>The group for an extension; unrecognised extensions land in Other.</summary>
    public static string GroupOf(string name, ExtensionKind kind) =>
        Groups.TryGetValue(name, out var group)
            ? group
            : Other;

    /// <summary>Sort position of a group (unknown groups last).</summary>
    public static int OrderOf(string group)
    {
        var i = Array.IndexOf(Order, group);
        return i < 0 ? Order.Length : i;
    }
}
