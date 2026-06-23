using System.IO;
using WhySave.Core;
using WhySave.Storage.Repositories;
using WhySave.Tests.Storage;

namespace WhySave.Tests.Core;

public class DetectionPipelineTests : StorageTestBase, IDisposable
{
    private readonly string _tempDir;
    private readonly FakeTimeProvider _timeProvider;
    private readonly FilesRepository _filesRepo;
    private readonly AppMetaRepository _metaRepo;
    private readonly FileIngester _ingester;
    private readonly IdentityResolver _resolver;
    private readonly JunkFilter _junkFilter;
    private readonly DetectionPipeline _pipeline;

    public DetectionPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySavePipelineTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _timeProvider = new FakeTimeProvider();
        _filesRepo = new FilesRepository(Connection);
        _metaRepo = new AppMetaRepository(Connection);
        _metaRepo.Set("first_run_at", _timeProvider.UtcNow.AddHours(-1).ToUnixTimeMilliseconds().ToString());
        _resolver = new IdentityResolver(_filesRepo);
        _ingester = new FileIngester(_resolver, _filesRepo, _metaRepo);
        _junkFilter = new JunkFilter();
        _pipeline = new DetectionPipeline(_ingester, _junkFilter, _filesRepo, _timeProvider);
    }

    public new void Dispose()
    {
        _pipeline.Dispose();
        base.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CrDownload_Renamed_To_Pdf_Ingests_Once_At_Final_Name()
    {
        var crdownloadPath = Path.Combine(_tempDir, "report.crdownload");
        var pdfPath = Path.Combine(_tempDir, "report.pdf");

        File.WriteAllBytes(crdownloadPath, new byte[2048]);
        await _pipeline.HandleEventAsync(new FileEvent(crdownloadPath, FileEventKind.Created, _timeProvider.UtcNow), default);

        File.Move(crdownloadPath, pdfPath);
        await _pipeline.HandleEventAsync(new FileEvent(pdfPath, FileEventKind.Renamed, _timeProvider.UtcNow), default);

        _timeProvider.Advance(DetectionPipeline.QuietPeriod + TimeSpan.FromMilliseconds(50));
        await _pipeline.WaitForPendingChecksAsync();

        var records = _filesRepo.ListByStatus("pending").ToList();
        Assert.Single(records);
        Assert.Equal(pdfPath, records[0].Path);
        Assert.Equal("report.pdf", records[0].Filename);
    }

    [Fact]
    public async Task Still_Growing_File_Reschedules_And_Ingests_When_Stable()
    {
        var path = Path.Combine(_tempDir, "growing.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        await _pipeline.HandleEventAsync(new FileEvent(path, FileEventKind.Created, _timeProvider.UtcNow), default);

        // Grow the file before the first quiet-period check fires.
        File.WriteAllBytes(path, new byte[4096]);
        _timeProvider.Advance(DetectionPipeline.QuietPeriod + TimeSpan.FromMilliseconds(50));

        // No ingest yet because size changed; a new check has been scheduled.
        Assert.Empty(_filesRepo.ListByStatus("pending"));

        // Now stable.
        _timeProvider.Advance(DetectionPipeline.QuietPeriod + TimeSpan.FromMilliseconds(50));
        await _pipeline.WaitForPendingChecksAsync();

        var records = _filesRepo.ListByStatus("pending").ToList();
        Assert.Single(records);
        Assert.Equal(path, records[0].Path);
        Assert.Equal(4096, records[0].SizeBytes);
    }

    [Fact]
    public async Task Locked_File_Reschedules_And_Ingests_When_Unlocked()
    {
        var path = Path.Combine(_tempDir, "locked.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            await _pipeline.HandleEventAsync(new FileEvent(path, FileEventKind.Created, _timeProvider.UtcNow), default);

            _timeProvider.Advance(DetectionPipeline.QuietPeriod + TimeSpan.FromMilliseconds(50));
            // No ingest yet because file is locked; a new check has been scheduled.
            Assert.Empty(_filesRepo.ListByStatus("pending"));
        }
        finally
        {
            fs.Dispose();
        }

        _timeProvider.Advance(DetectionPipeline.QuietPeriod + TimeSpan.FromMilliseconds(50));
        await _pipeline.WaitForPendingChecksAsync();

        var records = _filesRepo.ListByStatus("pending").ToList();
        Assert.Single(records);
        Assert.Equal(path, records[0].Path);
    }

    [Fact]
    public async Task Extracted_Children_Are_Ignored_When_Archive_Exists()
    {
        var zipPath = Path.Combine(_tempDir, "Notes.zip");
        var childPath = Path.Combine(_tempDir, "child1.txt");

        File.WriteAllBytes(zipPath, new byte[2048]);
        await _pipeline.HandleEventAsync(new FileEvent(zipPath, FileEventKind.Created, _timeProvider.UtcNow), default);

        _timeProvider.Advance(DetectionPipeline.QuietPeriod + TimeSpan.FromMilliseconds(50));
        await _pipeline.WaitForPendingChecksAsync();

        File.WriteAllBytes(childPath, new byte[2048]);
        await _pipeline.HandleEventAsync(new FileEvent(childPath, FileEventKind.Created, _timeProvider.UtcNow), default);

        _timeProvider.Advance(DetectionPipeline.QuietPeriod + TimeSpan.FromMilliseconds(50));
        await _pipeline.WaitForPendingChecksAsync();

        var records = _filesRepo.ListAll().ToList();
        Assert.Single(records);
        Assert.Equal(zipPath, records[0].Path);
    }

    [Fact]
    public async Task Deleted_File_Marks_Record_Missing()
    {
        var path = Path.Combine(_tempDir, "doomed.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        await _pipeline.HandleEventAsync(new FileEvent(path, FileEventKind.Created, _timeProvider.UtcNow), default);
        _timeProvider.Advance(DetectionPipeline.QuietPeriod + TimeSpan.FromMilliseconds(50));
        await _pipeline.WaitForPendingChecksAsync();

        var before = _filesRepo.GetByPath(path);
        Assert.NotNull(before);
        Assert.NotEqual("missing", before!.Status);

        File.Delete(path);
        await _pipeline.HandleEventAsync(new FileEvent(path, FileEventKind.Deleted, _timeProvider.UtcNow), default);

        var after = _filesRepo.GetByPath(path);
        Assert.NotNull(after);
        Assert.Equal("missing", after!.Status);
        Assert.Equal("pending", after.PriorStatus);
    }

    [Fact]
    public async Task Pending_File_Raises_PendingFileDetected_Event()
    {
        var path = Path.Combine(_tempDir, "event.pdf");
        File.WriteAllBytes(path, new byte[2048]);

        string? detectedFileId = null;
        _pipeline.PendingFileDetected += (_, fileId) => detectedFileId = fileId;

        await _pipeline.HandleEventAsync(new FileEvent(path, FileEventKind.Created, _timeProvider.UtcNow), default);
        _timeProvider.Advance(DetectionPipeline.QuietPeriod + TimeSpan.FromMilliseconds(50));
        await _pipeline.WaitForPendingChecksAsync();

        var record = _filesRepo.GetByPath(path);
        Assert.NotNull(record);
        Assert.Equal(record!.Id, detectedFileId);
    }

    [Fact]
    public void Stop_Does_Not_Deadlock_When_Started_On_A_Synchronization_Context()
    {
        Exception? shutdownException = null;
        var shutdownThread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
                _pipeline.Start();
#pragma warning disable xUnit1031 // This test intentionally exercises the app's synchronous WPF shutdown path.
                _pipeline.StopAsync().GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            }
            catch (Exception ex)
            {
                shutdownException = ex;
            }
        })
        {
            IsBackground = true,
        };

        shutdownThread.Start();

        Assert.True(shutdownThread.Join(TimeSpan.FromSeconds(2)), "Pipeline shutdown deadlocked.");
        Assert.Null(shutdownException);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Models a UI thread that is blocked in synchronous shutdown and cannot pump callbacks.
        }
    }
}
