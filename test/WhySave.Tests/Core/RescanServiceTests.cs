using System.IO;
using WhySave.Core;
using WhySave.Storage.Repositories;
using WhySave.Tests.Storage;

namespace WhySave.Tests.Core;

public class RescanServiceTests : StorageTestBase, IDisposable
{
    private readonly string _tempDir;
    private readonly FakeTimeProvider _timeProvider;
    private readonly FilesRepository _filesRepo;
    private readonly AppMetaRepository _metaRepo;
    private readonly FileIngester _ingester;
    private readonly IdentityResolver _resolver;
    private readonly JunkFilter _junkFilter;
    private readonly RescanService _rescan;

    public RescanServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveRescanTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _timeProvider = new FakeTimeProvider();
        _filesRepo = new FilesRepository(Connection);
        _metaRepo = new AppMetaRepository(Connection);
        _resolver = new IdentityResolver(_filesRepo);
        _ingester = new FileIngester(_resolver, _filesRepo, _metaRepo);
        _junkFilter = new JunkFilter();
        _rescan = new RescanService(
            _ingester,
            _resolver,
            _filesRepo,
            _junkFilter,
            _metaRepo,
            _timeProvider,
            () => _tempDir);
    }

    public new void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task First_Run_Backfill_Marks_All_Existing_As_Legacy()
    {
        for (var i = 0; i < 10; i++)
        {
            var path = Path.Combine(_tempDir, $"existing_{i}.pdf");
            File.WriteAllBytes(path, new byte[2048].Select((_, idx) => (byte)(i + idx)).ToArray());
        }

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        var legacy = _filesRepo.ListByStatus("legacy").ToList();
        Assert.Equal(10, legacy.Count);
        Assert.Empty(_filesRepo.ListByStatus("pending"));

        var firstRunAt = _metaRepo.Get(RescanService.MetaFirstRunAt);
        Assert.NotNull(firstRunAt);
        Assert.True(long.TryParse(firstRunAt, out _));
    }

    [Fact]
    public async Task New_File_After_FirstRunAt_Becomes_Pending()
    {
        // Seed existing files.
        for (var i = 0; i < 5; i++)
        {
            var path = Path.Combine(_tempDir, $"existing_{i}.pdf");
            File.WriteAllBytes(path, new byte[2048].Select((_, idx) => (byte)(i + idx)).ToArray());
        }

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });
        Assert.Equal(5, _filesRepo.ListByStatus("legacy").Count());

        // Add a new file after first_run_at.
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var newPath = Path.Combine(_tempDir, "new-file.pdf");
        File.WriteAllBytes(newPath, new byte[2048]);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        var pending = _filesRepo.ListByStatus("pending").ToList();
        Assert.Single(pending);
        Assert.Equal(newPath, pending[0].Path);
    }

    [Fact]
    public async Task Deleted_File_Is_Marked_Missing()
    {
        var path = Path.Combine(_tempDir, "will-delete.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });
        Assert.Single(_filesRepo.ListByStatus("legacy"));

        File.Delete(path);
        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        var missing = _filesRepo.ListByStatus("missing").ToList();
        Assert.Single(missing);
        Assert.Equal(path, missing[0].Path);
        Assert.Equal("legacy", missing[0].PriorStatus);
    }

    [Fact]
    public async Task Hourly_Rescan_Updates_LastRescanAt()
    {
        var path = Path.Combine(_tempDir, "hourly.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });
        Assert.Null(_metaRepo.Get(RescanService.MetaLastRescanAt));

        await _rescan.HourlyRescanAsync(new[] { _tempDir });

        var lastRescan = _metaRepo.Get(RescanService.MetaLastRescanAt);
        Assert.NotNull(lastRescan);
        Assert.True(long.TryParse(lastRescan, out _));
    }

    [Fact]
    public async Task Junk_Files_Are_Skipped_In_Rescan()
    {
        var goodPath = Path.Combine(_tempDir, "good.pdf");
        var junkPath = Path.Combine(_tempDir, "report.crdownload");
        File.WriteAllBytes(goodPath, new byte[2048]);
        File.WriteAllBytes(junkPath, new byte[2048]);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        var all = _filesRepo.ListAll().ToList();
        Assert.Single(all);
        Assert.Equal(goodPath, all[0].Path);
    }
}
