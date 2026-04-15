using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Crypto_Monitor.Modules.MarketData.Infrastructure;

public sealed class WpfLogStore
{
    private readonly string _connectionString;

    public WpfLogStore(IConfiguration configuration)
    {
        var dbPath = configuration["WpfLogDb:Path"] ?? "wpf-logs.db";
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ClientLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                CorrelationId TEXT NOT NULL,
                Path TEXT NOT NULL,
                StatusCode INTEGER NOT NULL,
                Message TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync();
    }

    public async Task LogAsync(string correlationId, string path, int statusCode, string message)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ClientLogs (TimestampUtc, CorrelationId, Path, StatusCode, Message)
            VALUES ($timestampUtc, $correlationId, $path, $statusCode, $message);
            """;
        command.Parameters.AddWithValue("$timestampUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$correlationId", correlationId);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$statusCode", statusCode);
        command.Parameters.AddWithValue("$message", message);

        await command.ExecuteNonQueryAsync();
    }
}
