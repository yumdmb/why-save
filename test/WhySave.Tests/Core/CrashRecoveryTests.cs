using System.IO;
using System.Linq;
using WhySave.Core;
using WhySave.Storage;
using WhySave.Storage.Repositories;
using WhySave.Tests.Storage;

namespace WhySave.Tests.Core;

public class CrashRecoveryTests : StorageTestBase, IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly FakeTimeProvider _timeProvider;
    private FilesRepository _filesRepo = null!;
    private AppMetaRepository _metaRepo = null!;
    private FileIngester _ingester = null!;
    private IdentityResolver _resolver = null!;
    private JunkFilter _junkFilter = null!;
    private RescanService _rescan = null!;

    public CrashRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveCrashRecovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Connection.DataSource;
        _timeProvider = new FakeTimeProvider();
        RebuildServices();
    }

    private void RebuildServices()
    {
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

    private static byte[] UniqueContent(string seed)
    {
        var bytes = new byte[2048];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(seed[i % seed.Length] + i);
        return bytes;
    }

    private void WriteFile(string path) =>
        File.WriteAllBytes(path, UniqueContent(Path.GetFileName(path)));

    [Fact]
    public async Task Crash_MidRun_Relaunch_Restores_No_Duplicates()
    {
        var fileA = Path.Combine(_tempDir, "report.pdf");
        var fileB = Path.Combine(_tempDir, "notes.pdf");
        WriteFile(fileA);
        WriteFile(fileB);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });
        Assert.Equal(2, _filesRepo.ListAll().Count());

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        var fileC = Path.Combine(_tempDir, "new_after_crash.pdf");
        WriteFile(fileC);

        RebuildServices();

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        var all = _filesRepo.ListAll().ToList();
        Assert.Equal(3, all.Count);
        Assert.Equal(3, all.Select(r => r.Path).Distinct().Count());
    }

    [Fact]
    public async Task Crash_Preserves_Pending_Status()
    {
        var fileA = Path.Combine(_tempDir, "pending.pdf");
        WriteFile(fileA);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        var fileB = Path.Combine(_tempDir, "new_pending.pdf");
        WriteFile(fileB);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });
        var pendingBefore = _filesRepo.ListByStatus("pending").ToList();
        Assert.Single(pendingBefore);

        RebuildServices();

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        var pendingAfter = _filesRepo.ListByStatus("pending").ToList();
        Assert.Single(pendingAfter);
        Assert.Equal(fileB, pendingAfter[0].Path);
    }

    [Fact]
    public async Task Crash_Then_File_Deleted_Restores_Missing_Status()
    {
        var fileA = Path.Combine(_tempDir, "will_delete.pdf");
        WriteFile(fileA);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });
        Assert.Single(_filesRepo.ListAll());

        File.Delete(fileA);

        RebuildServices();

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        var missing = _filesRepo.ListByStatus("missing").ToList();
        Assert.Single(missing);
        Assert.Equal(fileA, missing[0].Path);
    }

    [Fact]
    public async Task Crash_Then_Restored_File_Recovers_From_Missing()
    {
        var fileA = Path.Combine(_tempDir, "restore_me.pdf");
        WriteFile(fileA);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });
        Assert.Single(_filesRepo.ListAll());

        File.Delete(fileA);
        RebuildServices();
        await _rescan.RunStartupDiffAsync(new[] { _tempDir });
        Assert.Single(_filesRepo.ListByStatus("missing"));

        WriteFile(fileA);
        RebuildServices();
        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        var missing = _filesRepo.ListByStatus("missing").ToList();
        Assert.Empty(missing);
        var restored = _filesRepo.ListAll().ToList();
        Assert.Single(restored);
    }

    [Fact]
    public async Task Crash_With_Contexted_File_Preserves_Status()
    {
        var fileA = Path.Combine(_tempDir, "contexted.pdf");
        WriteFile(fileA);

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });
        var record = _filesRepo.ListAll().First();
        record.Status = "contexted";
        record.Reason = "Important reason";
        _filesRepo.Update(record);

        RebuildServices();

        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        var all = _filesRepo.ListAll().ToList();
        Assert.Single(all);
        Assert.Equal("contexted", all[0].Status);
    }

    [Fact]
    public async Task Hourly_Rescan_After_Crash_Updates_LastRescanAt()
    {
        WriteFile(Path.Combine(_tempDir, "idle.pdf"));
        await _rescan.RunStartupDiffAsync(new[] { _tempDir });

        RebuildServices();

        await _rescan.HourlyRescanAsync(new[] { _tempDir });

        var lastRescan = _metaRepo.Get(RescanService.MetaLastRescanAt);
        Assert.NotNull(lastRescan);
        Assert.True(long.TryParse(lastRescan, out _));
    }
}
