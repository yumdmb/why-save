using System.IO;
using WhySave.Core;
using WhySave.Native;
using WhySave.Storage.Repositories;
using WhySave.Tests.Storage;

namespace WhySave.Tests.Core;

public class IdentityResolverTests : StorageTestBase, IDisposable
{
    private readonly string _tempDir;

    public IdentityResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveResolverTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public new void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private static IngestRequest WatcherRequest(string path, DateTimeOffset? happenedAt = null) =>
        new(path, Url: null, Referrer: null, TabTitle: null,
            happenedAt ?? DateTimeOffset.UtcNow, FileIngester.SourceWatcher);

    private (FileIngester ingester, FilesRepository repo, AppMetaRepository meta) NewIngester(
        Func<string, FileNtfsIdentity?>? ntfsProvider = null)
    {
        var filesRepo = new FilesRepository(Connection);
        var metaRepo = new AppMetaRepository(Connection);
        metaRepo.Set("first_run_at", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds().ToString());
        var resolver = ntfsProvider is null
            ? new IdentityResolver(filesRepo)
            : new IdentityResolver(filesRepo, ntfsProvider);
        return (new FileIngester(resolver, filesRepo, metaRepo), filesRepo, metaRepo);
    }

    [Fact]
    public async Task New_File_Produces_New_Record_With_Ntfs_Identity_And_Sha256()
    {
        var path = Path.Combine(_tempDir, "report.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        var (ingester, repo, _) = NewIngester();
        var result = await ingester.IngestAsync(WatcherRequest(path));

        Assert.True(result.IsNew);
        var record = repo.GetById(result.FileId);
        Assert.NotNull(record);
        Assert.NotNull(record!.VolumeSerial);
        Assert.NotNull(record.NtfsFileId);
        Assert.NotNull(record.Sha256);
        Assert.Equal("report.pdf", record.Filename);
        Assert.Equal(".pdf", record.Ext);
        Assert.Equal(2048, record.SizeBytes);
    }

    [Fact]
    public async Task Rename_On_Same_Volume_Resolves_To_Same_Row_Via_Ntfs_Id()
    {
        var path = Path.Combine(_tempDir, "before.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        var (ingester, repo, _) = NewIngester();
        var first = await ingester.IngestAsync(WatcherRequest(path));

        var renamed = Path.Combine(_tempDir, "after.pdf");
        File.Move(path, renamed);

        var second = await ingester.IngestAsync(WatcherRequest(renamed));

        Assert.False(second.IsNew);
        Assert.Equal(first.FileId, second.FileId);

        var allRows = repo.ListByStatus("pending").ToList();
        Assert.Single(allRows);

        var record = repo.GetById(first.FileId);
        Assert.NotNull(record);
        Assert.Equal(renamed, record!.Path);
        Assert.Equal("after.pdf", record.Filename);
    }

    [Fact]
    public async Task Cross_Volume_Move_Resolves_To_Same_Row_Via_Sha256()
    {
        var path = Path.Combine(_tempDir, "original.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        var (ingester, repo, _) = NewIngester();
        var first = await ingester.IngestAsync(WatcherRequest(path));
        var originalRecord = repo.GetById(first.FileId);
        Assert.NotNull(originalRecord);
        var originalHash = originalRecord!.Sha256;
        Assert.NotNull(originalHash);

        var newPath = Path.Combine(_tempDir, "moved.pdf");
        File.WriteAllBytes(newPath, new byte[2048]);
        var oldPath = path;
        File.Delete(oldPath);

        var (ingester2, repo2, _) = NewIngester(ForceNullNtfs);

        var second = await ingester2.IngestAsync(WatcherRequest(newPath));

        Assert.False(second.IsNew);
        Assert.Equal(first.FileId, second.FileId);

        var record = repo2.GetById(first.FileId);
        Assert.NotNull(record);
        Assert.Equal(newPath, record!.Path);
        Assert.Equal(originalHash, record.Sha256);

        var allRows = repo2.ListByStatus("pending").ToList();
        Assert.Single(allRows);
    }

    [Fact]
    public async Task Missing_Record_Is_Restored_When_File_Reappears()
    {
        var path = Path.Combine(_tempDir, "restore-me.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        var (ingester, repo, _) = NewIngester();
        var first = await ingester.IngestAsync(WatcherRequest(path));

        var record = repo.GetById(first.FileId);
        Assert.NotNull(record);
        Assert.Equal("pending", record!.Status);

        repo.MarkMissing(first.FileId);
        var missingRecord = repo.GetById(first.FileId);
        Assert.NotNull(missingRecord);
        Assert.Equal("missing", missingRecord!.Status);
        Assert.Equal("pending", missingRecord.PriorStatus);

        var second = await ingester.IngestAsync(WatcherRequest(path));

        Assert.False(second.IsNew);
        Assert.Equal("pending", second.Status);

        var restored = repo.GetById(first.FileId);
        Assert.NotNull(restored);
        Assert.Equal("pending", restored!.Status);
        Assert.Null(restored.PriorStatus);
    }

    [Fact]
    public async Task Missing_Contexted_Record_Restores_To_Contexted()
    {
        var path = Path.Combine(_tempDir, "contexted-file.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        var (ingester, repo, _) = NewIngester();
        var first = await ingester.IngestAsync(WatcherRequest(path));

        var record = repo.GetById(first.FileId);
        Assert.NotNull(record);
        record!.Status = "contexted";
        repo.Update(record);

        repo.MarkMissing(first.FileId);
        var missingRecord = repo.GetById(first.FileId);
        Assert.NotNull(missingRecord);
        Assert.Equal("missing", missingRecord!.Status);
        Assert.Equal("contexted", missingRecord.PriorStatus);

        var second = await ingester.IngestAsync(WatcherRequest(path));

        Assert.False(second.IsNew);
        Assert.Equal("contexted", second.Status);
    }

    [Fact]
    public async Task Network_Share_Falls_Back_To_Path_Then_Hash()
    {
        var path = Path.Combine(_tempDir, "network.txt");
        File.WriteAllBytes(path, new byte[2048]);

        var (ingester, repo, _) = NewIngester(ForceNullNtfs);
        var first = await ingester.IngestAsync(WatcherRequest(path));

        var record = repo.GetById(first.FileId);
        Assert.NotNull(record);
        Assert.Null(record!.VolumeSerial);
        Assert.Null(record.NtfsFileId);
        Assert.NotNull(record.Sha256);

        var newPath = Path.Combine(_tempDir, "network-renamed.txt");
        File.Move(path, newPath);

        var second = await ingester.IngestAsync(WatcherRequest(newPath));

        Assert.False(second.IsNew);
        Assert.Equal(first.FileId, second.FileId);

        var updated = repo.GetById(first.FileId);
        Assert.NotNull(updated);
        Assert.Equal(newPath, updated!.Path);
        Assert.Equal("network-renamed.txt", updated.Filename);
    }

    [Fact]
    public async Task Large_File_Does_Not_Get_Sha256()
    {
        var path = Path.Combine(_tempDir, "large.bin");
        var size = IdentityResolver.MaxHashableSizeBytes + 1024;
        using (var fs = File.Create(path))
        {
            fs.SetLength(size);
        }

        var (ingester, repo, _) = NewIngester();
        var result = await ingester.IngestAsync(WatcherRequest(path));

        Assert.True(result.IsNew);
        var record = repo.GetById(result.FileId);
        Assert.NotNull(record);
        Assert.Null(record!.Sha256);
        Assert.Equal(size, record.SizeBytes);
    }

    [Fact]
    public async Task Hash_Is_Not_Recomputed_For_Existing_File()
    {
        var path = Path.Combine(_tempDir, "cached.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        var (ingester, repo, _) = NewIngester();
        var first = await ingester.IngestAsync(WatcherRequest(path));

        var record = repo.GetById(first.FileId);
        Assert.NotNull(record);
        var firstHash = record!.Sha256;
        Assert.NotNull(firstHash);

        var second = await ingester.IngestAsync(WatcherRequest(path));
        Assert.False(second.IsNew);

        var updated = repo.GetById(first.FileId);
        Assert.NotNull(updated);
        Assert.Equal(firstHash, updated!.Sha256);
    }

    [Fact]
    public async Task Nonexistent_File_Produces_New_Record_With_Zero_Size()
    {
        var path = Path.Combine(_tempDir, "ghost.pdf");

        var (ingester, _, _) = NewIngester();
        var result = await ingester.IngestAsync(WatcherRequest(path));

        Assert.True(result.IsNew);
    }

    [Fact]
    public async Task New_File_Found_By_Path_After_Restore_From_Missing()
    {
        var path = Path.Combine(_tempDir, "path-restore.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        var (ingester, repo, _) = NewIngester(ForceNullNtfs);
        var first = await ingester.IngestAsync(WatcherRequest(path));

        repo.MarkMissing(first.FileId);

        var second = await ingester.IngestAsync(WatcherRequest(path));

        Assert.False(second.IsNew);
        Assert.NotEqual("missing", second.Status);
    }

    private static Func<string, FileNtfsIdentity?> ForceNullNtfs => _ => null;
}
