using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace WhySave.Storage;

public record Migration(int Number, string Name, string Sql);

public partial class DatabaseMigrator
{
    private readonly SqliteConnection _connection;
    private readonly Assembly _assembly;

    public DatabaseMigrator(SqliteConnection connection, Assembly? assembly = null)
    {
        _connection = connection;
        _assembly = assembly ?? typeof(DatabaseMigrator).Assembly;
    }

    public int MigrateAsync(CancellationToken ct = default)
    {
        _connection.Execute("PRAGMA journal_mode=WAL;");
        _connection.Execute("PRAGMA foreign_keys=ON;");
        _connection.Execute("PRAGMA busy_timeout=5000;");

        var current = _connection.ExecuteScalar<int>("PRAGMA user_version;");
        var migrations = LoadMigrations().Where(m => m.Number > current).OrderBy(m => m.Number).ToList();

        foreach (var migration in migrations)
        {
            ApplyMigration(migration);
        }

        return _connection.ExecuteScalar<int>("PRAGMA user_version;");
    }

    private void ApplyMigration(Migration migration)
    {
        var ddl = StripPragmaLines(migration.Sql);

        using var tx = _connection.BeginTransaction();
        try
        {
            if (!string.IsNullOrWhiteSpace(ddl))
                _connection.Execute(ddl, transaction: tx);

            _connection.Execute($"PRAGMA user_version = {migration.Number};", transaction: tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static string StripPragmaLines(string sql)
    {
        var lines = sql.Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase))
                continue;
            sb.AppendLine(line);
        }
        return sb.ToString().Trim();
    }

    protected virtual IReadOnlyList<Migration> LoadMigrations()
    {
        var prefix = $"{_assembly.GetName().Name}.Migrations.";
        var migrations = new List<Migration>();

        foreach (var name in _assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                continue;

            var number = ExtractNumber(name);
            if (number is null)
                continue;

            using var stream = _assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sql = reader.ReadToEnd();

            migrations.Add(new Migration(number.Value, name, sql));
        }

        return migrations;
    }

    private static int? ExtractNumber(string resourceName)
    {
        var match = MigrationNumberRegex().Match(resourceName);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"\.(\d+)_", RegexOptions.Compiled)]
    private static partial Regex MigrationNumberRegex();
}
