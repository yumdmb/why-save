using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.Core;

public sealed class FileIngester : IFileIngester
{
    public const string SourceWatcher = "watcher";
    public const string SourceExtension = "extension";

    private const string MetaFirstRunAt = "first_run_at";

    private readonly IIdentityResolver _identityResolver;
    private readonly FilesRepository _filesRepo;
    private readonly AppMetaRepository _metaRepo;

    public FileIngester(IIdentityResolver identityResolver, FilesRepository filesRepo, AppMetaRepository metaRepo)
    {
        _identityResolver = identityResolver;
        _filesRepo = filesRepo;
        _metaRepo = metaRepo;
    }

    public async Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            throw new ArgumentException("Path is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Source))
            throw new ArgumentException("Source is required.", nameof(request));

        var identity = await _identityResolver.ResolveAsync(request.Path, ct);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var happenedAtMs = request.HappenedAt.ToUnixTimeMilliseconds();

        if (identity.ExistingFileId is { } existingId &&
            _filesRepo.GetById(existingId) is { } existing)
        {
            return UpdateExisting(existing, request, identity, nowMs);
        }

        return InsertNew(request, identity, nowMs, happenedAtMs);
    }

    private IngestResult UpdateExisting(
        FileRecord existing, IngestRequest request, FileIdentity identity, long nowMs)
    {
        existing.Path = request.Path;
        existing.Filename = identity.Filename;
        existing.Ext = identity.Ext;
        existing.SizeBytes = identity.SizeBytes;
        existing.VolumeSerial = identity.VolumeSerial;
        existing.NtfsFileId = identity.NtfsFileId;
        if (identity.Sha256 is not null)
            existing.Sha256 = identity.Sha256;
        existing.UpdatedAt = nowMs;

        if (!string.IsNullOrEmpty(request.Url))
            existing.Url = request.Url;
        if (request.Referrer is not null)
            existing.Referrer = request.Referrer;
        if (request.TabTitle is not null)
            existing.TabTitle = request.TabTitle;

        if (existing.Status == "missing")
        {
            existing.Status = existing.PriorStatus ?? "pending";
            existing.PriorStatus = null;
        }

        _filesRepo.Update(existing);
        return new IngestResult(existing.Id, IsNew: false, existing.Status);
    }

    private IngestResult InsertNew(
        IngestRequest request, FileIdentity identity, long nowMs, long happenedAtMs)
    {
        var firstRunAtStr = _metaRepo.Get(MetaFirstRunAt);
        long? firstRunAt = firstRunAtStr is not null && long.TryParse(firstRunAtStr, out var fra)
            ? fra : null;

        string status;
        if (firstRunAt is null)
            status = "legacy";
        else if (happenedAtMs >= firstRunAt.Value)
            status = "pending";
        else
            status = "legacy";

        var record = new FileRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Path = request.Path,
            Filename = identity.Filename,
            Ext = identity.Ext,
            SizeBytes = identity.SizeBytes,
            VolumeSerial = identity.VolumeSerial,
            NtfsFileId = identity.NtfsFileId,
            Sha256 = identity.Sha256,
            Status = status,
            Project = null,
            Url = request.Url,
            Referrer = request.Referrer,
            TabTitle = request.TabTitle,
            ParentFileId = null,
            FirstSeenAt = happenedAtMs,
            SavedAt = happenedAtMs,
            LastPromptedAt = null,
            LastResolvedAt = null,
            LastOpenedViaAppAt = null,
            CreatedAt = nowMs,
            UpdatedAt = nowMs,
        };

        _filesRepo.Insert(record);
        return new IngestResult(record.Id, IsNew: true, record.Status);
    }
}
