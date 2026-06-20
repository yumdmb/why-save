using System.IO;

namespace WhySave.Core;

public sealed class FileWatchService : IDisposable
{
    public const int InitialBackoffMs = 2000;
    public const int MaxBackoffMs = 30000;
    public const int HealthyWindowMs = 60000;

    private readonly IFileEventSink _sink;
    private readonly Func<int, Task> _delay;
    private readonly Action<string, Exception?> _log;
    private readonly List<WatchEntry> _entries = new();
    private bool _disposed;

    public FileWatchService(IFileEventSink sink, Action<string, Exception?>? log = null)
        : this(sink, ms => Task.Delay(ms), log ?? ((_, _) => { }))
    {
    }

    public FileWatchService(IFileEventSink sink, Func<int, Task> delay, Action<string, Exception?> log)
    {
        _sink = sink;
        _delay = delay;
        _log = log;
    }

    public IReadOnlyList<string> WatchedFolders => _entries.Select(e => e.Folder).ToList();

    public void Start(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
            AddFolder(folder);
    }

    public void AddFolder(string folder)
    {
        if (_entries.Any(e => e.Folder == folder))
            return;

        if (!Directory.Exists(folder))
        {
            _log($"Skipping missing folder: {folder}", null);
            return;
        }

        var entry = new WatchEntry(folder, this);
        _entries.Add(entry);
        entry.Subscribe();
        _log($"Watching folder: {folder}", null);
    }

    public void RemoveFolder(string folder)
    {
        var idx = _entries.FindIndex(e => e.Folder == folder);
        if (idx < 0) return;
        var entry = _entries[idx];
        _entries.RemoveAt(idx);
        entry.Dispose();
        _log($"Stopped watching: {folder}", null);
    }

    internal IReadOnlyList<(string Folder, int BackoffMs)> GetBackoffState() =>
        _entries.Select(e => (e.Folder, e.CurrentBackoffMs)).ToList();

    private void HandleEvent(FileEvent evt) => _sink.Post(evt);

    internal async Task ResubscribeAsync(WatchEntry entry)
    {
        entry.DisposeWatcher();
        var delay = entry.CurrentBackoffMs;
        _log($"Re-subscribing to {entry.Folder} after {delay}ms backoff", entry.LastError);
        await _delay(delay);
        if (_disposed) return;
        entry.Subscribe();
        entry.IncreaseBackoff();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries)
            entry.Dispose();
        _entries.Clear();
    }

    internal sealed class WatchEntry : IDisposable
    {
        public string Folder { get; }
        public Exception? LastError { get; private set; }
        public int CurrentBackoffMs { get; private set; } = InitialBackoffMs;
        public DateTime LastSubscribeAt { get; private set; } = DateTime.UtcNow;

        private FileSystemWatcher? _watcher;
        private readonly FileWatchService _owner;
        private bool _disposed;

        public WatchEntry(string folder, FileWatchService owner)
        {
            Folder = folder;
            _owner = owner;
        }

        public void Subscribe()
        {
            if (_disposed) return;
            DisposeWatcher();

            _watcher = new FileSystemWatcher(Folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                               | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };

            _watcher.Created += (_, e) => OnCreated(e.FullPath);
            _watcher.Renamed += (_, e) => OnRenamed(e.FullPath);
            _watcher.Deleted += (_, e) => OnDeleted(e.FullPath);
            _watcher.Error += (_, e) => OnError(e);

            LastSubscribeAt = DateTime.UtcNow;
        }

        public void IncreaseBackoff()
        {
            CurrentBackoffMs = Math.Min(CurrentBackoffMs * 2, MaxBackoffMs);
        }

        public void ResetBackoff()
        {
            CurrentBackoffMs = InitialBackoffMs;
        }

        private void OnCreated(string fullPath)
        {
            if (Directory.Exists(fullPath)) return;
            MaybeResetBackoff();
            _owner.HandleEvent(new FileEvent(fullPath, FileEventKind.Created, DateTimeOffset.UtcNow));
        }

        private void OnRenamed(string fullPath)
        {
            if (Directory.Exists(fullPath)) return;
            MaybeResetBackoff();
            _owner.HandleEvent(new FileEvent(fullPath, FileEventKind.Renamed, DateTimeOffset.UtcNow));
        }

        private void OnDeleted(string fullPath)
        {
            MaybeResetBackoff();
            _owner.HandleEvent(new FileEvent(fullPath, FileEventKind.Deleted, DateTimeOffset.UtcNow));
        }

        private void OnError(ErrorEventArgs e)
        {
            LastError = e.GetException();
            _ = _owner.ResubscribeAsync(this);
        }

        private void MaybeResetBackoff()
        {
            if (DateTime.UtcNow - LastSubscribeAt > TimeSpan.FromMilliseconds(HealthyWindowMs))
                CurrentBackoffMs = InitialBackoffMs;
        }

        public void DisposeWatcher()
        {
            if (_watcher is null) return;
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
            catch { }
            _watcher = null;
        }

        public void Dispose()
        {
            _disposed = true;
            DisposeWatcher();
        }
    }
}
