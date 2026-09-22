using System.IO;
using Microsoft.Data.Sqlite;

namespace PhpManager;

public static class UrlDatabase
{
    private static string DbPath => Path.Combine(AppContext.BaseDirectory, "urls.db");
    private static string ConnectionString => $"Data Source={DbPath}";

    private static bool _initialized;

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
            """;
        cmd.ExecuteNonQuery();

        _initialized = true;

        SeedIfEmpty();
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
        {
            results.Add(new UrlRecord
            {
                Id = reader.GetInt64(0),
                Category = reader.GetString(1),
                Key = reader.GetString(2),
                UrlTemplate = reader.GetString(3),
                Description = reader.GetString(4),
                IsDefault = reader.GetInt32(5) == 1
            });
        }

        return results;
    }

    public static List<UrlRecord> GetByCategory(string category)
    {
        Initialize();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM urls WHERE Category = $cat ORDER BY Key";
        cmd.Parameters.AddWithValue("$cat", category);

        using var reader = cmd.ExecuteReader();
        var results = new List<UrlRecord>();

        while (reader.Read())
        {
            results.Add(new UrlRecord
            {
                Id = reader.GetInt64(0),
                Category = reader.GetString(1),
                Key = reader.GetString(2),
                UrlTemplate = reader.GetString(3),
                Description = reader.GetString(4),
                IsDefault = reader.GetInt32(5) == 1
            });
        }

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

        var result = cmd.ExecuteScalar();
        return result?.ToString();
    }

    public static UrlRecord? GetById(long id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM urls WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new UrlRecord
        {
            Id = reader.GetInt64(0),
            Category = reader.GetString(1),
            Key = reader.GetString(2),
            UrlTemplate = reader.GetString(3),
            Description = reader.GetString(4),
            IsDefault = reader.GetInt32(5) == 1
        };
    }

    public static void Insert(UrlRecord record)
    {
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
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM urls WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static void SeedIfEmpty()
    {
        var existing = GetAll();
        if (existing.Count > 0) return;

        var defaults = new List<UrlRecord>
        {
            new() { Category = "PHP Download", Key = "Mirror 1", UrlTemplate = "https://windows.php.net/downloads/releases/php-{0}-nts-Win32-vs17-x64.zip", Description = "Primary PHP download mirror", IsDefault = true },
            new() { Category = "PHP Download", Key = "Mirror 2", UrlTemplate = "https://downloads.php.net/~windows/releases/php-{0}-nts-Win32-vs17-x64.zip", Description = "Secondary PHP download mirror", IsDefault = false },
            new() { Category = "PHP Download", Key = "Mirror 3", UrlTemplate = "https://downloads.php.net/~windows/releases/archives/php-{0}-nts-Win32-vs17-x64.zip", Description = "Archive PHP download mirror", IsDefault = false },
            new() { Category = "PHP Catalog", Key = "Mirror 1", UrlTemplate = "https://windows.php.net/downloads/releases/", Description = "Primary catalog index", IsDefault = true },
            new() { Category = "PHP Catalog", Key = "Mirror 2", UrlTemplate = "https://downloads.php.net/~windows/releases/", Description = "Secondary catalog index", IsDefault = false },
            new() { Category = "PHP Catalog", Key = "Mirror 3", UrlTemplate = "https://downloads.php.net/~windows/releases/archives/", Description = "Archive catalog index", IsDefault = false },
            new() { Category = "Redis", Key = "Download", UrlTemplate = "https://windows.php.net/downloads/pecl/releases/redis/{version}/{package}.zip", Description = "Redis extension. Tokens: {version}, {package}", IsDefault = true },
            new() { Category = "SQL Server", Key = "Download", UrlTemplate = "https://github.com/microsoft/msphpsql/releases/download/v{version}/Windows_{version}RTW.zip", Description = "SQL Server drivers. Token: {version}", IsDefault = true },
            new() { Category = "FrankenPHP", Key = "Download", UrlTemplate = "https://github.com/php/frankenphp/releases/{path}/frankenphp-windows-x86_64.zip", Description = "FrankenPHP runtime. Token: {path}", IsDefault = true },
            new() { Category = "Servy", Key = "Download", UrlTemplate = "https://github.com/nicholasgasior/servy/releases/latest/download/servy-cli-windows-amd64.zip", Description = "Servy CLI for service management", IsDefault = true },
            new() { Category = "CA Certificate", Key = "Download", UrlTemplate = "https://curl.se/ca/cacert.pem", Description = "Mozilla CA certificate bundle", IsDefault = true },
        };

        foreach (var record in defaults)
            Insert(record);
    }

    public static void SeedDefaults()
    {
        Initialize();
        SeedIfEmpty();
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
}
