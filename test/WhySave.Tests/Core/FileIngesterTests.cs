using WhySave.Core;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;
using WhySave.Tests.Storage;

namespace WhySave.Tests.Core;

public class FileIngesterTests : StorageTestBase
{
    private static readonly FileIdentity NewFileIdentity = new(
        ExistingFileId: null,
        SizeBytes: 2048,
        Filename: "report.pdf",
        Ext: ".pdf",
        VolumeSerial: 12345,
        NtfsFileId: 67890,
        Sha256: "abc123");

    private static IngestRequest WatcherRequest(string path = "/downloads/report.pdf", DateTimeOffset? happenedAt = null) =>
        new(path, Url: null, Referrer: null, TabTitle: null, happenedAt ?? DateTimeOffset.UtcNow, SourceWatcher);

    private const string SourceWatcher = "watcher";

    [Fact]
    public async Task Watcher_Source_With_FirstRunAt_Produces_Pending_Row()
    {
        var metaRepo = new AppMetaRepository(Connection);
        var firstRunAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        metaRepo.Set("first_run_at", firstRunAt.ToString());

        var filesRepo = new FilesRepository(Connection);
        var ingester = new FileIngester(new StubResolver(NewFileIdentity), filesRepo, metaRepo);

        var result = await ingester.IngestAsync(WatcherRequest());

        Assert.True(result.IsNew);
        Assert.Equal("pending", result.Status);

        var record = filesRepo.GetById(result.FileId);
        Assert.NotNull(record);
        Assert.Equal("pending", record!.Status);
        Assert.Equal("/downloads/report.pdf", record.Path);
        Assert.Equal("report.pdf", record.Filename);
        Assert.Equal(".pdf", record.Ext);
        Assert.Equal(2048, record.SizeBytes);
        Assert.Equal(12345, record.VolumeSerial);
        Assert.Equal(67890, record.NtfsFileId);
        Assert.Equal("abc123", record.Sha256);
        Assert.Null(record.Url);
        Assert.Null(record.ReasonCipher);
        Assert.Null(record.NotesCipher);
    }

    [Fact]
    public async Task Watcher_Source_Without_FirstRunAt_Produces_Legacy_Row()
    {
        var metaRepo = new AppMetaRepository(Connection);
        var filesRepo = new FilesRepository(Connection);
        var ingester = new FileIngester(new StubResolver(NewFileIdentity), filesRepo, metaRepo);

        var result = await ingester.IngestAsync(WatcherRequest());

        Assert.True(result.IsNew);
        Assert.Equal("legacy", result.Status);

        var record = filesRepo.GetById(result.FileId);
        Assert.NotNull(record);
        Assert.Equal("legacy", record!.Status);
    }

    [Fact]
    public async Task Watcher_Source_Older_Than_FirstRunAt_Produces_Legacy_Row()
    {
        var metaRepo = new AppMetaRepository(Connection);
        var firstRunAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        metaRepo.Set("first_run_at", firstRunAt.ToString());

        var filesRepo = new FilesRepository(Connection);
        var ingester = new FileIngester(new StubResolver(NewFileIdentity), filesRepo, metaRepo);

        var oldTime = DateTimeOffset.UtcNow.AddHours(-2);
        var result = await ingester.IngestAsync(WatcherRequest(happenedAt: oldTime));

        Assert.True(result.IsNew);
        Assert.Equal("legacy", result.Status);
    }

    [Fact]
    public async Task Extension_Source_Stores_Url_Referrer_TabTitle()
    {
        var metaRepo = new AppMetaRepository(Connection);
        metaRepo.Set("first_run_at", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds().ToString());

        var filesRepo = new FilesRepository(Connection);
        var ingester = new FileIngester(new StubResolver(NewFileIdentity), filesRepo, metaRepo);

        var request = new IngestRequest(
            Path: "/downloads/paper.pdf",
            Url: "https://example.com/page",
            Referrer: "https://google.com",
            TabTitle: "Research Tab",
            HappenedAt: DateTimeOffset.UtcNow,
            Source: "extension");

        var result = await ingester.IngestAsync(request);

        Assert.True(result.IsNew);
        Assert.Equal("pending", result.Status);

        var record = filesRepo.GetById(result.FileId);
        Assert.NotNull(record);
        Assert.Equal("https://example.com/page", record!.Url);
        Assert.Equal("https://google.com", record.Referrer);
        Assert.Equal("Research Tab", record.TabTitle);
    }

    [Fact]
    public async Task Existing_Record_Is_Updated_Not_Duplicated()
    {
        var metaRepo = new AppMetaRepository(Connection);
        metaRepo.Set("first_run_at", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds().ToString());

        var filesRepo = new FilesRepository(Connection);
        var ingester = new FileIngester(new StubResolver(NewFileIdentity), filesRepo, metaRepo);

        var first = await ingester.IngestAsync(WatcherRequest());
        var identity = NewFileIdentity with { ExistingFileId = first.FileId, Filename = "renamed.pdf" };
        var ingester2 = new FileIngester(new StubResolver(identity), filesRepo, metaRepo);

        var second = await ingester2.IngestAsync(WatcherRequest(path: "/downloads/renamed.pdf"));

        Assert.False(second.IsNew);
        Assert.Equal(first.FileId, second.FileId);

        var allRows = filesRepo.ListByStatus("pending").ToList();
        Assert.Single(allRows);

        var record = filesRepo.GetById(first.FileId);
        Assert.NotNull(record);
        Assert.Equal("/downloads/renamed.pdf", record!.Path);
        Assert.Equal("renamed.pdf", record.Filename);
    }

    [Fact]
    public async Task Missing_Record_Is_Restored_On_Reappearance()
    {
        var metaRepo = new AppMetaRepository(Connection);
        metaRepo.Set("first_run_at", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds().ToString());

        var filesRepo = new FilesRepository(Connection);
        var ingester = new FileIngester(new StubResolver(NewFileIdentity), filesRepo, metaRepo);

        var first = await ingester.IngestAsync(WatcherRequest());

        var record = filesRepo.GetById(first.FileId);
        Assert.NotNull(record);
        record!.Status = "missing";
        filesRepo.Update(record);

        var identity = NewFileIdentity with { ExistingFileId = first.FileId };
        var ingester2 = new FileIngester(new StubResolver(identity), filesRepo, metaRepo);
        var result = await ingester2.IngestAsync(WatcherRequest());

        Assert.False(result.IsNew);
        Assert.Equal("pending", result.Status);

        var restored = filesRepo.GetById(first.FileId);
        Assert.NotNull(restored);
        Assert.Equal("pending", restored!.Status);
    }

    [Fact]
    public async Task Empty_Path_Throws()
    {
        var metaRepo = new AppMetaRepository(Connection);
        var filesRepo = new FilesRepository(Connection);
        var ingester = new FileIngester(new StubResolver(NewFileIdentity), filesRepo, metaRepo);

        await Assert.ThrowsAsync<ArgumentException>(
            () => ingester.IngestAsync(WatcherRequest(path: "")));
    }

    [Fact]
    public async Task FirstSeenAt_And_SavedAt_Set_To_HappenedAt()
    {
        var metaRepo = new AppMetaRepository(Connection);
        metaRepo.Set("first_run_at", "0");

        var filesRepo = new FilesRepository(Connection);
        var ingester = new FileIngester(new StubResolver(NewFileIdentity), filesRepo, metaRepo);

        var happened = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var result = await ingester.IngestAsync(WatcherRequest(happenedAt: happened));

        var record = filesRepo.GetById(result.FileId);
        Assert.NotNull(record);
        Assert.Equal(happened.ToUnixTimeMilliseconds(), record!.FirstSeenAt);
        Assert.Equal(happened.ToUnixTimeMilliseconds(), record.SavedAt);
    }

    private sealed class StubResolver : IIdentityResolver
    {
        private readonly FileIdentity _identity;
        public StubResolver(FileIdentity identity) => _identity = identity;
        public Task<FileIdentity> ResolveAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(_identity);
    }
}
