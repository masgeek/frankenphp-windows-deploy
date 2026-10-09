using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhpManager;

public static class UrlDatabase
{
    private static string DbPath => Path.Combine(AppContext.BaseDirectory, "urls.db");
    private static string ConnectionString => $"Data Source={DbPath}";

    private static bool _initialized;

    private static readonly (string Category, string Key, string Url, string Description, bool IsDefault)[] Defaults =
    {
        ("PHP Download", "Mirror 1", "https://windows.php.net/downloads/releases/php-{0}-nts-Win32-vs17-x64.zip", "Primary PHP download mirror. Token: {0} = version", true),
        ("PHP Download", "Mirror 2", "https://downloads.php.net/~windows/releases/php-{0}-nts-Win32-vs17-x64.zip", "Secondary PHP download mirror", false),
        ("PHP Download", "Mirror 3", "https://downloads.php.net/~windows/releases/archives/php-{0}-nts-Win32-vs17-x64.zip", "Archive PHP download mirror", false),

        ("PHP Catalog", "Mirror 1", "https://windows.php.net/downloads/releases/", "Primary catalog index", true),
        ("PHP Catalog", "Mirror 2", "https://downloads.php.net/~windows/releases/", "Secondary catalog index", false),
        ("PHP Catalog", "Mirror 3", "https://downloads.php.net/~windows/releases/archives/", "Archive catalog index", false),

        ("Redis", "Download", "https://windows.php.net/downloads/pecl/releases/redis/{version}/{package}.zip", "Redis extension package. Tokens: {version}, {package}", true),
        ("Redis", "Version Index", "https://windows.php.net/downloads/pecl/releases/redis/", "Directory index used to list available Redis versions", true),

        ("Xdebug", "Download", "https://windows.php.net/downloads/pecl/releases/xdebug/{version}/{package}.zip", "Xdebug extension package. Tokens: {version}, {package}", true),
        ("Xdebug", "Version Index", "https://windows.php.net/downloads/pecl/releases/xdebug/", "Directory index used to list available Xdebug versions", true),

        ("SQL Server", "Download", "https://github.com/microsoft/msphpsql/releases/download/v{version}/Windows_{version}RTW.zip", "SQL Server drivers. Token: {version}", true),

        ("FrankenPHP", "Download", "https://github.com/php/frankenphp/releases/{path}/frankenphp-windows-x86_64.zip", "FrankenPHP runtime. Token: {path}", true),
        ("Servy", "Download", "https://github.com/nicholasgasior/servy/releases/latest/download/servy-cli-windows-amd64.zip", "Servy CLI for service management", true),
        ("CA Certificate", "Download", "https://curl.se/ca/cacert.pem", "Mozilla CA certificate bundle", true),

        ("Documentation", "Redis", "https://pecl.php.net/package/redis", "Redis extension documentation and releases", true),
        ("Documentation", "Xdebug", "https://xdebug.org/docs/", "Xdebug documentation", true),
        ("Documentation", "Xdebug Wizard", "https://xdebug.org/wizard", "Official installation wizard", true),
        ("Documentation", "SQL Server", "https://github.com/microsoft/msphpsql/releases", "SQL Server driver releases", true),
        ("Documentation", "Servy", "https://github.com/nicholasgasior/servy", "Servy project repository", true),
        ("Documentation", "FrankenPHP", "https://frankenphp.dev/", "FrankenPHP documentation", true),
        ("Documentation", "PHP Windows", "https://windows.php.net/downloads/releases/", "PHP for Windows releases", true)
    };

    public static void Initialize()
    {
        if (_initialized) return;

        var dir = Path.GetDirectoryName(DbPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS urls (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Category TEXT NOT NULL,
                Key TEXT NOT NULL,
                UrlTemplate TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                IsDefault INTEGER NOT NULL DEFAULT 0,
                UNIQUE(Category, Key)
            );
            CREATE TABLE IF NOT EXISTS app_config (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        _initialized = true;

        EnsureDefaults();
    }

    /// <summary>Inserts only default rows that are missing, so user customisations are preserved.</summary>
    public static void EnsureDefaults()
    {
        Initialize();

        foreach (var row in Defaults)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO urls (Category, Key, UrlTemplate, Description, IsDefault)
                VALUES ($cat, $key, $url, $desc, $isDefault);
                """;
            cmd.Parameters.AddWithValue("$cat", row.Category);
            cmd.Parameters.AddWithValue("$key", row.Key);
            cmd.Parameters.AddWithValue("$url", row.Url);
            cmd.Parameters.AddWithValue("$desc", row.Description);
            cmd.Parameters.AddWithValue("$isDefault", row.IsDefault ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Deletes every stored URL and restores the shipped defaults.</summary>
    public static void ResetToDefaults()
    {
        Initialize();

        using (var connection = new SqliteConnection(ConnectionString))
        {
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM urls; DELETE FROM sqlite_sequence WHERE name = 'urls';";
            cmd.ExecuteNonQuery();
        }

        EnsureDefaults();
    }

    public static List<UrlRecord> GetAll()
    {
        Initialize();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM urls ORDER BY Category, Key";

        using var reader = cmd.ExecuteReader();
        var results = new List<UrlRecord>();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    public static List<UrlRecord> GetByCategory(string category)
    {
        Initialize();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM urls WHERE Category = $cat ORDER BY IsDefault DESC, Key";
        cmd.Parameters.AddWithValue("$cat", category);

        using var reader = cmd.ExecuteReader();
        var results = new List<UrlRecord>();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    public static string? GetUrl(string category, string key)
    {
        Initialize();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT UrlTemplate FROM urls WHERE Category = $cat AND Key = $key";
        cmd.Parameters.AddWithValue("$cat", category);
        cmd.Parameters.AddWithValue("$key", key);

        return cmd.ExecuteScalar()?.ToString();
    }

    public static UrlRecord? GetById(long id)
    {
        Initialize();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM urls WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public static void Insert(UrlRecord record)
    {
        Initialize();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO urls (Category, Key, UrlTemplate, Description, IsDefault)
            VALUES ($cat, $key, $url, $desc, $isDefault);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$cat", record.Category);
        cmd.Parameters.AddWithValue("$key", record.Key);
        cmd.Parameters.AddWithValue("$url", record.UrlTemplate);
        cmd.Parameters.AddWithValue("$desc", record.Description);
        cmd.Parameters.AddWithValue("$isDefault", record.IsDefault ? 1 : 0);

        record.Id = (long)(cmd.ExecuteScalar() ?? 0);
    }

    public static void Update(UrlRecord record)
    {
        Initialize();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE urls SET Category = $cat, Key = $key, UrlTemplate = $url,
                Description = $desc, IsDefault = $isDefault
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$cat", record.Category);
        cmd.Parameters.AddWithValue("$key", record.Key);
        cmd.Parameters.AddWithValue("$url", record.UrlTemplate);
        cmd.Parameters.AddWithValue("$desc", record.Description);
        cmd.Parameters.AddWithValue("$isDefault", record.IsDefault ? 1 : 0);

        cmd.ExecuteNonQuery();
    }

    public static void Delete(long id)
    {
        Initialize();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM urls WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static List<string> GetCategories()
    {
        Initialize();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT Category FROM urls ORDER BY Category";

        using var reader = cmd.ExecuteReader();
        var categories = new List<string>();
        while (reader.Read())
            categories.Add(reader.GetString(0));

        return categories;
    }

    private static UrlRecord Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Category = reader.GetString(1),
        Key = reader.GetString(2),
        UrlTemplate = reader.GetString(3),
        Description = reader.GetString(4),
        IsDefault = reader.GetInt32(5) == 1
    };

    // ---------- export / import ----------

    public static string ExportJson()
    {
        var payload = new
        {
            exportedUtc = DateTime.UtcNow.ToString("O"),
            urls = GetAll().Select(u => new
            {
                u.Category, u.Key, u.UrlTemplate, u.Description, u.IsDefault
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static int ImportJson(string json, bool overwriteExisting = true)
    {
        var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("urls", out var urls) || urls.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The file does not contain a 'urls' array.");

        var imported = 0;
        foreach (var item in urls.EnumerateArray())
        {
            var category = item.GetProperty("Category").GetString() ?? "";
            var key = item.GetProperty("Key").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(key))
                continue;

            var url = item.GetProperty("UrlTemplate").GetString() ?? "";
            var description = item.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";
            var isDefault = item.TryGetProperty("IsDefault", out var f) && f.ValueKind == JsonValueKind.True;

            var existing = GetByCategory(category).FirstOrDefault(r => r.Key == key);
            if (existing != null)
            {
                if (!overwriteExisting) continue;
                existing.UrlTemplate = url;
                existing.Description = description;
                Update(existing);
            }
            else
            {
                Insert(new UrlRecord
                {
                    Category = category,
                    Key = key,
                    UrlTemplate = url,
                    Description = description,
                    IsDefault = isDefault
                });
            }

            imported++;
        }

        return imported;
    }
}