using System.IO;
using System.Threading.Channels;
using WhySave.Storage.Repositories;

namespace WhySave.Core;

public sealed class DetectionPipeline : IDisposable
{
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan ArchiveChildWindow = TimeSpan.FromSeconds(30);

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz"
    };

    private readonly Channel<FileEvent> _channel;
    private readonly IFileIngester _ingester;
    private readonly JunkFilter _junkFilter;
    private readonly FilesRepository _filesRepo;
    private readonly ITimeProvider _timeProvider;
    private readonly Action<string, Exception?> _log;
    private readonly Dictionary<string, PendingCheck> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private Task? _consumeTask;
    private int _inFlightChecks;
    private bool _disposed;

    public DetectionPipeline(
        IFileIngester ingester,
        JunkFilter junkFilter,
        FilesRepository filesRepo,
        ITimeProvider? timeProvider = null,
        Action<string, Exception?>? log = null)
    {
        _ingester = ingester;
        _junkFilter = junkFilter;
        _filesRepo = filesRepo;
        _timeProvider = timeProvider ?? SystemTimeProvider.Instance;
        _log = log ?? ((_, _) => { });
        _channel = Channel.CreateUnbounded<FileEvent>();
    }

    public Channel<FileEvent> Input => _channel;

    public event EventHandler<string>? PendingFileDetected;

    public void Post(FileEvent evt) => _channel.Writer.TryWrite(evt);

    public void Start(CancellationToken externalCt = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _consumeTask = Task.Run(() => ConsumeLoopAsync(externalCt));
    }

    public async Task StopAsync()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        if (_consumeTask is not null)
            try { await _consumeTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
    }

    private async Task ConsumeLoopAsync(CancellationToken externalCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, externalCt);
        var ct = linked.Token;

        await foreach (var evt in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await HandleEventAsync(evt, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log($"Detection pipeline error handling event {evt.Path}", ex);
            }
        }
    }

    internal async Task HandleEventAsync(FileEvent evt, CancellationToken ct)
    {
        var path = Path.GetFullPath(evt.Path);

        if (evt.Kind == FileEventKind.Deleted)
        {
            _log($"File deleted: {path}", null);
            var existing = _filesRepo.GetByPath(path);
            if (existing is not null && existing.Status != "missing")
                _filesRepo.MarkMissing(existing.Id);
            return;
        }

        if (!File.Exists(path))
            return;

        var info = new FileInfo(path);
        if (_junkFilter.IsJunk(path, info.Length))
        {
            _log($"Junk filtered: {path}", null);
            return;
        }

        var ext = Path.GetExtension(path);
        if (!ArchiveExtensions.Contains(ext) && HasRecentArchiveInDirectory(info.DirectoryName))
        {
            _log($"Ignoring likely extracted child: {path}", null);
            return;
        }

        PendingCheck check;
        lock (_pending)
        {
            var generation = _pending.TryGetValue(path, out var old) ? old.Generation + 1 : 1;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
            check = new PendingCheck(path, info.Length, generation, _timeProvider.UtcNow + QuietPeriod, cts);
            _pending[path] = check;
        }

        RunQuietPeriodCheckAsync(check, ct);
    }

    private void RunQuietPeriodCheckAsync(PendingCheck check, CancellationToken ct)
    {
        Interlocked.Increment(ref _inFlightChecks);
        _ = RunQuietPeriodCheckCoreAsync(check, ct);
    }

    private async Task RunQuietPeriodCheckCoreAsync(PendingCheck check, CancellationToken ct)
    {
        try
        {
            var delay = check.ScheduledAt - _timeProvider.UtcNow;
            if (delay > TimeSpan.Zero)
                await _timeProvider.Delay(delay, check.Cts.Token);

            lock (_pending)
            {
                if (!_pending.TryGetValue(check.Path, out var current) || current.Generation != check.Generation)
                    return;
            }

            if (!File.Exists(check.Path))
                return;

            var info = new FileInfo(check.Path);
            var currentSize = info.Length;
            var locked = IsFileLocked(check.Path);

            if (locked || currentSize != check.LastSize)
            {
                _log($"File still active (locked={locked}, sizeChanged={currentSize != check.LastSize}): {check.Path}", null);

                PendingCheck next;
                lock (_pending)
                {
                    if (!_pending.TryGetValue(check.Path, out var current) || current.Generation != check.Generation)
                        return;

                    var nextGen = check.Generation + 1;
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
                    next = new PendingCheck(check.Path, currentSize, nextGen, _timeProvider.UtcNow + QuietPeriod, cts);
                    _pending[check.Path] = next;
                }

                RunQuietPeriodCheckAsync(next, ct);
                return;
            }

            lock (_pending)
            {
                if (_pending.TryGetValue(check.Path, out var current) && current.Generation == check.Generation)
                    _pending.Remove(check.Path);
            }

            _log($"Quiet period satisfied, ingesting: {check.Path}", null);
            var result = await _ingester.IngestAsync(new IngestRequest(
                check.Path, null, null, null, _timeProvider.UtcNow, FileIngester.SourceWatcher), ct);

            if (result.Status == "pending")
            {
                _log($"Pending file detected, raising toast for: {check.Path}", null);
                PendingFileDetected?.Invoke(this, result.FileId);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when timer is reset or pipeline is stopping.
        }
        catch (Exception ex)
        {
            _log($"Quiet-period check failed for {check.Path}", ex);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightChecks);
        }
    }

    internal async Task WaitForPendingChecksAsync(TimeSpan? timeout = null)
    {
        var realTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < realTimeout.TotalMilliseconds
               && Interlocked.CompareExchange(ref _inFlightChecks, 0, 0) > 0)
        {
            // Yield to the thread pool so fire-and-forget check tasks can run.
            await Task.Yield();
        }
    }

    private bool HasRecentArchiveInDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return false;

        var now = _timeProvider.UtcNow;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!ArchiveExtensions.Contains(Path.GetExtension(file)))
                    continue;

                var created = File.GetCreationTimeUtc(file);
                if (now - created < ArchiveChildWindow)
                    return true;
            }
        }
        catch { }

        return false;
    }

    public static bool IsFileLocked(string path)
    {
        try
        {
            using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _channel.Writer.TryComplete();
        _cts.Dispose();
    }

    private sealed class PendingCheck
    {
        public string Path { get; }
        public long LastSize { get; }
        public int Generation { get; }
        public DateTimeOffset ScheduledAt { get; }
        public CancellationTokenSource Cts { get; }

        public PendingCheck(string path, long lastSize, int generation, DateTimeOffset scheduledAt, CancellationTokenSource cts)
        {
            Path = path;
            LastSize = lastSize;
            Generation = generation;
            ScheduledAt = scheduledAt;
            Cts = cts;
        }
    }
}
