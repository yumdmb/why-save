using Dapper;
using Microsoft.Data.Sqlite;

namespace WhySave.Storage;

public static class SqliteConnectionFactory
{
    public static SqliteConnection Create(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        connection.Execute("PRAGMA journal_mode=WAL;");
        connection.Execute("PRAGMA foreign_keys=ON;");
        connection.Execute("PRAGMA busy_timeout=5000;");
        return connection;
    }
}
