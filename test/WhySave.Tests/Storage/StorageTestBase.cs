using Microsoft.Data.Sqlite;
using WhySave.Storage;

namespace WhySave.Tests.Storage;

public abstract class StorageTestBase : IDisposable
{
    protected readonly SqliteConnection Connection;
    private readonly string _tempPath;

    protected StorageTestBase()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "WhySaveTestDb_" + Guid.NewGuid().ToString("N") + ".db");
        Connection = SqliteConnectionFactory.Create(_tempPath);
        new DatabaseMigrator(Connection).MigrateAsync();
    }

    public void Dispose()
    {
        Connection.Dispose();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _tempPath + ext;
            if (File.Exists(p))
            {
                try { File.Delete(p); } catch { }
            }
        }
    }
}
