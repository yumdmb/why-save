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
        var statements = SplitStatements(migration.Sql);

        foreach (var stmt in statements.Where(IsPragma))
            _connection.Execute(stmt);

        using var tx = _connection.BeginTransaction();
        try
        {
            foreach (var stmt in statements.Where(s => !IsPragma(s)))
                _connection.Execute(stmt, transaction: tx);

            _connection.Execute($"PRAGMA user_version = {migration.Number};", transaction: tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static bool IsPragma(string statement)
    {
        var trimmed = statement.TrimStart();
        return trimmed.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase);
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

    private static List<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        var sb = new StringBuilder();
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            sb.Append(c);

            if (inSingle)
            {
                if (c == '\'')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        sb.Append(sql[++i]);
                    else
                        inSingle = false;
                }
            }
            else if (inDouble)
            {
                if (c == '"')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '"')
                        sb.Append(sql[++i]);
                    else
                        inDouble = false;
                }
            }
            else
            {
                if (c == '\'') inSingle = true;
                else if (c == '"') inDouble = true;
                else if (c == ';')
                {
                    var stmt = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(stmt) && !stmt.StartsWith("--", StringComparison.Ordinal))
                        statements.Add(stmt);
                    sb.Clear();
                }
            }
        }

        var last = sb.ToString().Trim();
        if (!string.IsNullOrEmpty(last) && !last.StartsWith("--", StringComparison.Ordinal))
            statements.Add(last);

        return statements;
    }

    [GeneratedRegex(@"\.(\d+)_", RegexOptions.Compiled)]
    private static partial Regex MigrationNumberRegex();
}
