using System.IO;
using Microsoft.Data.Sqlite;

namespace PhpManager;

public static class FrankenPhpDatabase
{
    private static string DbPath => Path.Combine(AppContext.BaseDirectory, "frankenphp-services.db");

    private static string ConnectionString => $"Data Source={DbPath}";

    public static void Initialize()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS services (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ServiceName TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                AppPath TEXT NOT NULL,
                Port INTEGER NOT NULL DEFAULT 8001,
                AdminPort INTEGER NOT NULL DEFAULT 2020,
                Workers INTEGER NOT NULL DEFAULT 2,
                MaxRequests INTEGER NOT NULL DEFAULT 250,
                HealthPath TEXT NOT NULL DEFAULT '/up',
                FirewallRuleName TEXT NOT NULL DEFAULT '',
                IsDevelopment INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public static List<FrankenPhpServiceRecord> GetAll()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM services ORDER BY ServiceName";

        using var reader = cmd.ExecuteReader();
        var results = new List<FrankenPhpServiceRecord>();

        while (reader.Read())
        {
            results.Add(new FrankenPhpServiceRecord
            {
                Id = reader.GetInt64(0),
                ServiceName = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Description = reader.GetString(3),
                AppPath = reader.GetString(4),
                Port = reader.GetInt32(5),
                AdminPort = reader.GetInt32(6),
                Workers = reader.GetInt32(7),
                MaxRequests = reader.GetInt32(8),
                HealthPath = reader.GetString(9),
                FirewallRuleName = reader.GetString(10),
                IsDevelopment = reader.GetInt32(11) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(12)),
                UpdatedAt = DateTime.Parse(reader.GetString(13))
            });
        }

        return results;
    }

    public static FrankenPhpServiceRecord? GetByServiceName(string serviceName)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM services WHERE ServiceName = $name";
        cmd.Parameters.AddWithValue("$name", serviceName);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new FrankenPhpServiceRecord
        {
            Id = reader.GetInt64(0),
            ServiceName = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Description = reader.GetString(3),
            AppPath = reader.GetString(4),
            Port = reader.GetInt32(5),
            AdminPort = reader.GetInt32(6),
            Workers = reader.GetInt32(7),
            MaxRequests = reader.GetInt32(8),
            HealthPath = reader.GetString(9),
            FirewallRuleName = reader.GetString(10),
            IsDevelopment = reader.GetInt32(11) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(12)),
            UpdatedAt = DateTime.Parse(reader.GetString(13))
        };
    }

    public static FrankenPhpServiceRecord Insert(FrankenPhpServiceRecord record)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO services (ServiceName, DisplayName, Description, AppPath, Port, AdminPort, Workers, MaxRequests, HealthPath, FirewallRuleName, IsDevelopment, CreatedAt, UpdatedAt)
            VALUES ($serviceName, $displayName, $description, $appPath, $port, $adminPort, $workers, $maxRequests, $healthPath, $firewallRuleName, $isDevelopment, datetime('now'), datetime('now'));
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$serviceName", record.ServiceName);
        cmd.Parameters.AddWithValue("$displayName", record.DisplayName);
        cmd.Parameters.AddWithValue("$description", record.Description);
        cmd.Parameters.AddWithValue("$appPath", record.AppPath);
        cmd.Parameters.AddWithValue("$port", record.Port);
        cmd.Parameters.AddWithValue("$adminPort", record.AdminPort);
        cmd.Parameters.AddWithValue("$workers", record.Workers);
        cmd.Parameters.AddWithValue("$maxRequests", record.MaxRequests);
        cmd.Parameters.AddWithValue("$healthPath", record.HealthPath);
        cmd.Parameters.AddWithValue("$firewallRuleName", record.FirewallRuleName);
        cmd.Parameters.AddWithValue("$isDevelopment", record.IsDevelopment ? 1 : 0);

        record.Id = (long)(cmd.ExecuteScalar() ?? 0);
        return record;
    }

    public static void Update(FrankenPhpServiceRecord record)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE services SET
                ServiceName = $serviceName,
                DisplayName = $displayName,
                Description = $description,
                AppPath = $appPath,
                Port = $port,
                AdminPort = $adminPort,
                Workers = $workers,
                MaxRequests = $maxRequests,
                HealthPath = $healthPath,
                FirewallRuleName = $firewallRuleName,
                IsDevelopment = $isDevelopment,
                UpdatedAt = datetime('now')
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$serviceName", record.ServiceName);
        cmd.Parameters.AddWithValue("$displayName", record.DisplayName);
        cmd.Parameters.AddWithValue("$description", record.Description);
        cmd.Parameters.AddWithValue("$appPath", record.AppPath);
        cmd.Parameters.AddWithValue("$port", record.Port);
        cmd.Parameters.AddWithValue("$adminPort", record.AdminPort);
        cmd.Parameters.AddWithValue("$workers", record.Workers);
        cmd.Parameters.AddWithValue("$maxRequests", record.MaxRequests);
        cmd.Parameters.AddWithValue("$healthPath", record.HealthPath);
        cmd.Parameters.AddWithValue("$firewallRuleName", record.FirewallRuleName);
        cmd.Parameters.AddWithValue("$isDevelopment", record.IsDevelopment ? 1 : 0);

        cmd.ExecuteNonQuery();
    }

    public static void Delete(long id)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM services WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static bool ExistsByServiceName(string serviceName, long? excludeId = null)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        if (excludeId.HasValue)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM services WHERE ServiceName = $name AND Id != $excludeId";
            cmd.Parameters.AddWithValue("$excludeId", excludeId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM services WHERE ServiceName = $name";
        }
        cmd.Parameters.AddWithValue("$name", serviceName);

        return (long)(cmd.ExecuteScalar() ?? 0) > 0;
    }

    public static bool PortInUse(int port, long? excludeId = null)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        if (excludeId.HasValue)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM services WHERE (Port = $port OR AdminPort = $port) AND Id != $excludeId";
            cmd.Parameters.AddWithValue("$excludeId", excludeId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM services WHERE Port = $port OR AdminPort = $port";
        }
        cmd.Parameters.AddWithValue("$port", port);

        return (long)(cmd.ExecuteScalar() ?? 0) > 0;
    }
}
