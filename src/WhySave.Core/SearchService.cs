using System.Text.RegularExpressions;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.Core;

public sealed class SearchService
{
    public const int DefaultLimit = 200;
    public const int ReasonSnippetLength = 120;

    private static readonly Regex ReasonScopeRegex =
        new(@"reason:""([^""]*)""|reason:(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly FilesRepository _filesRepository;

    public SearchService(FilesRepository filesRepository)
    {
        _filesRepository = filesRepository;
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string rawQuery, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<SearchResult>>(() =>
        {
            if (string.IsNullOrWhiteSpace(rawQuery))
                return Array.Empty<SearchResult>();

            var (reasonTerm, ftsQuery) = ParseQuery(rawQuery);

            IEnumerable<FileRecord> candidates = string.IsNullOrWhiteSpace(ftsQuery)
                ? _filesRepository.ListAll().Take(DefaultLimit)
                : _filesRepository.SearchFts(ftsQuery, DefaultLimit);

            if (!string.IsNullOrEmpty(reasonTerm))
            {
                candidates = candidates.Where(r =>
                    !string.IsNullOrEmpty(r.Reason) &&
                    r.Reason.Contains(reasonTerm, StringComparison.OrdinalIgnoreCase));
            }

            return candidates
                .Select(ToSearchResult)
                .ToList();
        }, cancellationToken);
    }

    internal static (string? ReasonTerm, string FtsQuery) ParseQuery(string query)
    {
        var match = ReasonScopeRegex.Match(query);
        if (!match.Success)
            return (null, query.Trim());

        var reasonTerm = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        var ftsQuery = query.Replace(match.Value, "", StringComparison.Ordinal).Trim();
        return (reasonTerm, ftsQuery);
    }

    private static SearchResult ToSearchResult(FileRecord r) =>
        new()
        {
            FileId = r.Id,
            Path = r.Path,
            Filename = r.Filename,
            ReasonSnippet = Snippet(r.Reason),
            Project = r.Project,
            SavedAt = r.SavedAt.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(r.SavedAt.Value)
                : null,
            Status = r.Status,
            StatusBadge = StatusBadgeFor(r.Status),
        };

    internal static string? Snippet(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
            return null;
        return reason.Length > ReasonSnippetLength
            ? reason.Substring(0, ReasonSnippetLength) + "…"
            : reason;
    }

    internal static string StatusBadgeFor(string status) => status switch
    {
        "pending" => "Pending",
        "contexted" => "Contexted",
        "legacy" => "Legacy",
        "missing" => "Missing",
        _ => status,
    };
}
