using System.IO;
using System.Threading.Channels;
using WhySave.Core;

namespace WhySave.Tests.Core;

public class FileWatchServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ChannelFileEventSink _sink;
    private readonly Channel<FileEvent> _channel;

    public FileWatchServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveWatchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _channel = Channel.CreateUnbounded<FileEvent>();
        _sink = new ChannelFileEventSink(_channel);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private async Task<FileEvent> WaitForEventAsync(int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"No event within {timeoutMs}ms");
        }
    }

    [Fact]
    public async Task Start_Watches_Default_Folder()
    {
        using var svc = new FileWatchService(_sink);
        svc.Start(new[] { _tempDir });

        Assert.Single(svc.WatchedFolders);
        Assert.Equal(_tempDir, svc.WatchedFolders[0]);

        var path = Path.Combine(_tempDir, "created.txt");
        File.WriteAllText(path, "hello");

        var evt = await WaitForEventAsync();
        Assert.Equal(FileEventKind.Created, evt.Kind);
        Assert.Equal(path, evt.Path);
    }

    [Fact]
    public async Task Created_File_Raises_Created_Event()
    {
        using var svc = new FileWatchService(_sink);
        svc.AddFolder(_tempDir);

        var path = Path.Combine(_tempDir, "new-file.pdf");
        File.WriteAllText(path, "content");

        var evt = await WaitForEventAsync();
        Assert.Equal(FileEventKind.Created, evt.Kind);
        Assert.Equal(path, evt.Path);
    }

    [Fact]
    public async Task Renamed_File_Raises_Renamed_Event_With_New_Path()
    {
        using var svc = new FileWatchService(_sink);
        svc.AddFolder(_tempDir);

        var oldPath = Path.Combine(_tempDir, "before.txt");
        File.WriteAllText(oldPath, "rename me");
        await WaitForEventAsync();

        var newPath = Path.Combine(_tempDir, "after.txt");
        File.Move(oldPath, newPath);

        var evt = await WaitForEventAsync();
        Assert.Equal(FileEventKind.Renamed, evt.Kind);
        Assert.Equal(newPath, evt.Path);
    }

    [Fact]
    public async Task Deleted_File_Raises_Deleted_Event()
    {
        using var svc = new FileWatchService(_sink);
        svc.AddFolder(_tempDir);

        var path = Path.Combine(_tempDir, "doomed.txt");
        File.WriteAllText(path, "delete me");
        await WaitForEventAsync();

        File.Delete(path);

        var evt = await WaitForEventAsync();
        Assert.Equal(FileEventKind.Deleted, evt.Kind);
        Assert.Equal(path, evt.Path);
    }

    [Fact]
    public async Task AddFolder_After_Start_Watches_New_Folder()
    {
        using var svc = new FileWatchService(_sink);
        svc.Start(Array.Empty<string>());

        var subDir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(subDir);
        svc.AddFolder(subDir);

        var path = Path.Combine(subDir, "nested.txt");
        File.WriteAllText(path, "nested content");

        var evt = await WaitForEventAsync();
        Assert.Equal(path, evt.Path);
    }

    [Fact]
    public async Task RemoveFolder_Stops_Watching()
    {
        using var svc = new FileWatchService(_sink);
        svc.AddFolder(_tempDir);

        var path = Path.Combine(_tempDir, "before-remove.txt");
        File.WriteAllText(path, "test");
        await WaitForEventAsync();

        svc.RemoveFolder(_tempDir);

        var anotherPath = Path.Combine(_tempDir, "after-remove.txt");
        File.WriteAllText(anotherPath, "should not be watched");

        await Assert.ThrowsAsync<TimeoutException>(() => WaitForEventAsync(1000));
    }

    [Fact]
    public async Task AddFolder_Duplicate_Is_Ignored()
    {
        using var svc = new FileWatchService(_sink);
        svc.AddFolder(_tempDir);
        svc.AddFolder(_tempDir);

        Assert.Single(svc.WatchedFolders);
    }

    [Fact]
    public async Task AddFolder_Nonexistent_Is_Skipped()
    {
        using var svc = new FileWatchService(_sink);
        svc.AddFolder(Path.Combine(_tempDir, "does-not-exist"));

        Assert.Empty(svc.WatchedFolders);
    }

    [Fact]
    public async Task Directory_Creation_Is_Filtered_Out()
    {
        using var svc = new FileWatchService(_sink);
        svc.AddFolder(_tempDir);

        var subDir = Path.Combine(_tempDir, "new-folder");
        Directory.CreateDirectory(subDir);

        await Assert.ThrowsAsync<TimeoutException>(() => WaitForEventAsync(1000));
    }

    [Fact]
    public async Task Error_Triggers_Resubscribe_With_Backoff()
    {
        var delays = new List<int>();
        using var svc = new FileWatchService(
            _sink,
            ms =>
            {
                delays.Add(ms);
                return Task.CompletedTask;
            },
            (_, _) => { });

        svc.AddFolder(_tempDir);

        var entry = svc.GetType()
            .GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(svc) as List<FileWatchService.WatchEntry>;
        Assert.NotNull(entry);
        Assert.Single(entry);

        var watchEntry = entry![0];
        watchEntry.Subscribe();

        var initialBackoff = watchEntry.CurrentBackoffMs;
        Assert.Equal(FileWatchService.InitialBackoffMs, initialBackoff);

        await svc.ResubscribeAsync(watchEntry);

        Assert.NotEmpty(delays);
        Assert.Equal(FileWatchService.InitialBackoffMs, delays[0]);
        Assert.Equal(FileWatchService.InitialBackoffMs * 2, watchEntry.CurrentBackoffMs);
    }

    [Fact]
    public async Task Repeated_Errors_Cap_At_Max_Backoff()
    {
        var delays = new List<int>();
        using var svc = new FileWatchService(
            _sink,
            ms =>
            {
                delays.Add(ms);
                return Task.CompletedTask;
            },
            (_, _) => { });

        svc.AddFolder(_tempDir);
        var entryField = svc.GetType()
            .GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(svc) as List<FileWatchService.WatchEntry>;
        var watchEntry = entryField![0];

        for (var i = 0; i < 10; i++)
            await svc.ResubscribeAsync(watchEntry);

        Assert.All(delays, d => Assert.True(d <= FileWatchService.MaxBackoffMs));
        Assert.Equal(FileWatchService.MaxBackoffMs, watchEntry.CurrentBackoffMs);
    }

    [Fact]
    public async Task Healthy_Events_After_Window_Reset_Backoff()
    {
        var delays = new List<int>();
        using var svc = new FileWatchService(
            _sink,
            ms =>
            {
                delays.Add(ms);
                return Task.CompletedTask;
            },
            (_, _) => { });

        svc.AddFolder(_tempDir);
        var entryField = svc.GetType()
            .GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(svc) as List<FileWatchService.WatchEntry>;
        var watchEntry = entryField![0];

        await svc.ResubscribeAsync(watchEntry);
        await svc.ResubscribeAsync(watchEntry);
        Assert.Equal(FileWatchService.InitialBackoffMs * 4, watchEntry.CurrentBackoffMs);

        var subscribeField = typeof(FileWatchService.WatchEntry).GetField(
            "<LastSubscribeAt>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        subscribeField?.SetValue(watchEntry, DateTime.UtcNow.AddSeconds(-70));

        watchEntry.ResetBackoff();
        Assert.Equal(FileWatchService.InitialBackoffMs, watchEntry.CurrentBackoffMs);
    }

    [Fact]
    public async Task Multiple_Folders_Each_Watched_Independently()
    {
        var dir1 = Path.Combine(_tempDir, "folder1");
        var dir2 = Path.Combine(_tempDir, "folder2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        using var svc = new FileWatchService(_sink);
        svc.Start(new[] { dir1, dir2 });

        Assert.Equal(2, svc.WatchedFolders.Count);

        var path1 = Path.Combine(dir1, "in1.txt");
        File.WriteAllText(path1, "one");

        var evt1 = await WaitForEventAsync();
        Assert.Equal(path1, evt1.Path);

        var path2 = Path.Combine(dir2, "in2.txt");
        File.WriteAllText(path2, "two");

        var evt2 = await WaitForEventAsync();
        Assert.Equal(path2, evt2.Path);
    }

    [Fact]
    public async Task Dispose_Stops_All_Watchers()
    {
        var svc = new FileWatchService(_sink);
        svc.AddFolder(_tempDir);

        var path = Path.Combine(_tempDir, "before-dispose.txt");
        File.WriteAllText(path, "test");
        await WaitForEventAsync();

        svc.Dispose();

        var anotherPath = Path.Combine(_tempDir, "after-dispose.txt");
        File.WriteAllText(anotherPath, "should not be watched");

        await Assert.ThrowsAsync<TimeoutException>(() => WaitForEventAsync(1000));
    }
}
