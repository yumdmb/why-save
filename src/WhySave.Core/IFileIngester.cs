namespace WhySave.Core;

public record IngestRequest(
    string Path,
    string? Url,
    string? Referrer,
    string? TabTitle,
    DateTimeOffset HappenedAt,
    string Source);

public record IngestResult(
    string FileId,
    bool IsNew,
    string Status);

public interface IFileIngester
{
    Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken ct = default);
}
