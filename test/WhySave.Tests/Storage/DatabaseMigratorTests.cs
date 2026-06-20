using Dapper;
using Microsoft.Data.Sqlite;
using WhySave.Storage;

namespace WhySave.Tests.Storage;

public class DatabaseMigratorTests : StorageTestBase
{
    [Fact]
    public void Fresh_Db_Applies_All_Migrations_And_Sets_UserVersion()
    {
        var version = Connection.ExecuteScalar<int>("PRAGMA user_version;");
        Assert.True(version >= 3);

        var tables = Connection.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").ToList();

        Assert.Contains("files", tables);
        Assert.Contains("files_fts", tables);
        Assert.Contains("app_meta", tables);
    }

    [Fact]
    public void Fts_Table_Has_Expected_Columns()
    {
        var columns = Connection.Query<string>(
            "SELECT name FROM pragma_table_info('files_fts') ORDER BY cid").ToList();

        Assert.Contains("filename", columns);
        Assert.Contains("project", columns);
        Assert.Contains("url", columns);
    }

    [Fact]
    public void Fts_Triggers_Keep_Mirror_In_Sync_On_Insert()
    {
        Connection.Execute(
            "INSERT INTO files (id, path, filename, ext, size_bytes, status, first_seen_at, created_at, updated_at) " +
            "VALUES ('t1', '/p/AI_Paper.pdf', 'AI_Paper.pdf', '.pdf', 1024, 'pending', 1000, 1000, 1000)");

        var match = Connection.QueryFirstOrDefault<string>(
            "SELECT filename FROM files_fts WHERE files_fts MATCH 'AI_Paper'");

        Assert.Equal("AI_Paper.pdf", match);
    }

    [Fact]
    public void Fts_Triggers_Keep_Mirror_In_Sync_On_Update()
    {
        Connection.Execute(
            "INSERT INTO files (id, path, filename, ext, size_bytes, status, first_seen_at, created_at, updated_at) " +
            "VALUES ('t2', '/p/AI_Paper.pdf', 'AI_Paper.pdf', '.pdf', 1024, 'pending', 1000, 1000, 1000)");

        Connection.Execute("UPDATE files SET project = 'ML-course' WHERE id = 't2'");

        var match = Connection.QueryFirstOrDefault<string>(
            "SELECT filename FROM files_fts WHERE files_fts MATCH 'ML-course'");
        Assert.Equal("AI_Paper.pdf", match);

        Connection.Execute("UPDATE files SET project = 'data-science' WHERE id = 't2'");
        var oldMatch = Connection.QueryFirstOrDefault<string>(
            "SELECT filename FROM files_fts WHERE files_fts MATCH 'ML-course'");
        Assert.Null(oldMatch);
    }

    [Fact]
    public void Fts_Triggers_Keep_Mirror_In_Sync_On_Delete()
    {
        Connection.Execute(
            "INSERT INTO files (id, path, filename, ext, size_bytes, status, first_seen_at, created_at, updated_at) " +
            "VALUES ('t3', '/p/Notes.txt', 'Notes.txt', '.txt', 512, 'pending', 1000, 1000, 1000)");

        Connection.Execute("DELETE FROM files WHERE id = 't3'");

        var match = Connection.QueryFirstOrDefault<string>(
            "SELECT filename FROM files_fts WHERE files_fts MATCH 'Notes'");
        Assert.Null(match);
    }

    [Fact]
    public void Migrate_Is_Idempotent_On_Already_Migrated_Db()
    {
        var versionBefore = Connection.ExecuteScalar<int>("PRAGMA user_version;");

        new DatabaseMigrator(Connection).MigrateAsync();

        var versionAfter = Connection.ExecuteScalar<int>("PRAGMA user_version;");
        Assert.Equal(versionBefore, versionAfter);
    }

    [Fact]
    public void Failing_Migration_Rolls_Back_While_Earlier_Remain()
    {
        using var conn = SqliteConnectionFactory.Create(
            Path.Combine(Path.GetTempPath(), "WhySaveFail_" + Guid.NewGuid().ToString("N") + ".db"));
        var migrator = new FailingMigrator(conn);

        Assert.Throws<SqliteException>(() => migrator.MigrateAsync());

        var version = conn.ExecuteScalar<int>("PRAGMA user_version;");
        Assert.Equal(1, version);

        var tableA = conn.QueryFirstOrDefault<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='test_a'");
        Assert.Equal("test_a", tableA);

        var tableB = conn.QueryFirstOrDefault<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='test_b'");
        Assert.Null(tableB);
    }

    private sealed class FailingMigrator : DatabaseMigrator
    {
        private static readonly Migration[] _migrations =
        {
            new(1, "test_0001", "CREATE TABLE test_a (id INTEGER);"),
            new(2, "test_0002", "CREATE TABLE test_b (id INTEGER); CREATE TABLE test_b (id INTEGER);"),
            new(3, "test_0003", "CREATE TABLE test_c (id INTEGER);"),
        };

        public FailingMigrator(SqliteConnection conn) : base(conn) { }

        protected override IReadOnlyList<Migration> LoadMigrations() => _migrations;
    }
}
