using System.IO;
using WhySave.Storage.Repositories;

namespace WhySave.Core;

public sealed class RescanService
{
    public const string MetaFirstRunAt = "first_run_at";
    public const string MetaLastRescanAt = "last_rescan_at";

    private readonly IFileIngester _ingester;
    private readonly IIdentityResolver _identityResolver;
    private readonly FilesRepository _filesRepo;
    private readonly JunkFilter _junkFilter;
    private readonly AppMetaRepository _metaRepo;
    private readonly ITimeProvider _timeProvider;
    private readonly Func<string> _downloadsPathProvider;
    private readonly Action<string, Exception?> _log;

    public RescanService(
        IFileIngester ingester,
        IIdentityResolver identityResolver,
        FilesRepository filesRepo,
        JunkFilter junkFilter,
        AppMetaRepository metaRepo,
        ITimeProvider? timeProvider = null,
        Func<string>? downloadsPathProvider = null,
        Action<string, Exception?>? log = null)
    {
        _ingester = ingester;
        _identityResolver = identityResolver;
        _filesRepo = filesRepo;
        _junkFilter = junkFilter;
        _metaRepo = metaRepo;
        _timeProvider = timeProvider ?? SystemTimeProvider.Instance;
        _downloadsPathProvider = downloadsPathProvider ?? GetDefaultDownloadsPath;
        _log = log ?? ((_, _) => { });
    }

    public async Task RunStartupDiffAsync(IEnumerable<string> watchedFolders, CancellationToken ct = default)
    {
        _log("Running startup diff", null);

        if (_metaRepo.Get(MetaFirstRunAt) is null)
        {
            await RunFirstRunBackfillAsync(ct);
        }

        await RunDiffAsync(watchedFolders, ct);
    }

    public async Task HourlyRescanAsync(IEnumerable<string> watchedFolders, CancellationToken ct = default)
    {
        _log("Running hourly rescan", null);
        await RunDiffAsync(watchedFolders, ct);
        _metaRepo.Set(MetaLastRescanAt, _timeProvider.UtcNow.ToUnixTimeMilliseconds().ToString());
    }

    private async Task RunFirstRunBackfillAsync(CancellationToken ct)
    {
        var now = _timeProvider.UtcNow;
        _metaRepo.Set(MetaFirstRunAt, now.ToUnixTimeMilliseconds().ToString());
        _log($"First run at set to {now:O}", null);

        var downloads = _downloadsPathProvider();
        if (!Directory.Exists(downloads))
        {
            _log($"Default Downloads folder does not exist: {downloads}", null);
            return;
        }

        foreach (var file in EnumerateNonJunkFiles(downloads))
        {
            ct.ThrowIfCancellationRequested();

            // Use a timestamp just before first_run_at so existing files are always legacy.
            var happenedAt = now.AddSeconds(-1);
            _log($"Backfilling legacy file: {file}", null);
            await _ingester.IngestAsync(new IngestRequest(
                file, null, null, null, happenedAt, FileIngester.SourceWatcher), ct);
        }
    }

    private async Task RunDiffAsync(IEnumerable<string> watchedFolders, CancellationToken ct)
    {
        var touchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in watchedFolders)
        {
            ct.ThrowIfCancellationRequested();

            if (!Directory.Exists(folder))
            {
                _log($"Watched folder missing during diff: {folder}", null);
                continue;
            }

            foreach (var file in EnumerateNonJunkFiles(folder))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var happenedAt = GetFileCreationTime(file);
                    var result = await _ingester.IngestAsync(new IngestRequest(
                        file, null, null, null, happenedAt, FileIngester.SourceWatcher), ct);
                    touchedIds.Add(result.FileId);
                }
                catch (Exception ex)
                {
                    _log($"Failed to ingest during rescan: {file}", ex);
                }
            }
        }

        foreach (var record in _filesRepo.ListAll())
        {
            ct.ThrowIfCancellationRequested();

            if (touchedIds.Contains(record.Id))
                continue;

            if (record.Status == "missing")
                continue;

            if (!File.Exists(record.Path))
            {
                _log($"Marking missing: {record.Path}", null);
                _filesRepo.MarkMissing(record.Id);
            }
        }
    }

    private IEnumerable<string> EnumerateNonJunkFiles(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            long size;
            try
            {
                size = new FileInfo(file).Length;
            }
            catch
            {
                continue;
            }

            if (_junkFilter.IsJunk(file, size))
            {
                _log($"Junk filtered during rescan: {file}", null);
                continue;
            }

            yield return file;
        }
    }

    private DateTimeOffset GetFileCreationTime(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetCreationTimeUtc(path), TimeSpan.Zero);
        }
        catch
        {
            return _timeProvider.UtcNow;
        }
    }

    public static string GetDefaultDownloadsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
}
