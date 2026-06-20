using Dapper;
using Microsoft.Data.Sqlite;

namespace WhySave.Storage.Repositories;

public class AppMetaRepository
{
    private readonly SqliteConnection _connection;

    public AppMetaRepository(SqliteConnection connection)
    {
        _connection = connection;
    }

    public string? Get(string key) =>
        _connection.QueryFirstOrDefault<string>(
            "SELECT value FROM app_meta WHERE key = @key", new { key });

    public void Set(string key, string? value)
    {
        _connection.Execute(
            """
            INSERT INTO app_meta (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """,
            new { key, value });
    }
}
