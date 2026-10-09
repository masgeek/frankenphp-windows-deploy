using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhpManager;

/// <summary>Generic key/value store (JSON values) used for tunables that are not plain URLs.</summary>
public static class AppConfig
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
            CREATE TABLE IF NOT EXISTS app_config (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        _initialized = true;
    }

    public static T? Get<T>(string key) where T : class
    {
        Initialize();

        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Value FROM app_config WHERE Key = $key";
            cmd.Parameters.AddWithValue("$key", key);

            var raw = cmd.ExecuteScalar() as string;
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.Deserialize<T>(raw);
        }
        catch
        {
            return null;
        }
    }

    public static void Set<T>(string key, T value)
    {
        Initialize();

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_config (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
        cmd.ExecuteNonQuery();
    }

    public static string? GetRaw(string key) => Get<Dictionary<string, string>>(key) is { } d && d.TryGetValue("value", out var v) ? v : null;
}