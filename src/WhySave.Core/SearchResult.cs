namespace WhySave.Core;

public sealed record SearchResult
{
    public required string FileId { get; init; }
    public required string Path { get; init; }
    public required string Filename { get; init; }
    public required string? ReasonSnippet { get; init; }
    public required string? Project { get; init; }
    public required DateTimeOffset? SavedAt { get; init; }
    public required string Status { get; init; }
    public required string StatusBadge { get; init; }
}
