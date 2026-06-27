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

    public Task<IReadOnlyList<SearchResult>> BrowseAsync(int limit = DefaultLimit, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<SearchResult>>(() =>
        {
            return _filesRepository.ListAll()
                .Where(IsFindDefaultRecord)
                .Take(limit)
                .Select(ToSearchResult)
                .ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string rawQuery, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<SearchResult>>(() =>
        {
            if (string.IsNullOrWhiteSpace(rawQuery))
                return Array.Empty<SearchResult>();

            var (reasonTerm, ftsQuery) = ParseQuery(rawQuery);

            IEnumerable<FileRecord> candidates;
            if (string.IsNullOrWhiteSpace(ftsQuery))
            {
                candidates = _filesRepository.ListAll().Take(DefaultLimit);
            }
            else
            {
                candidates = MergeCandidates(
                    _filesRepository.SearchFts(ftsQuery, DefaultLimit),
                    string.IsNullOrEmpty(reasonTerm)
                        ? _filesRepository.ListAll().Where(r => MatchesContextFields(r, ftsQuery))
                        : []);
            }

            if (!string.IsNullOrEmpty(reasonTerm))
            {
                candidates = candidates.Where(r =>
                    !string.IsNullOrEmpty(r.Reason) &&
                    r.Reason.Contains(reasonTerm, StringComparison.OrdinalIgnoreCase));
            }

            return candidates
                .Take(DefaultLimit)
                .Select(ToSearchResult)
                .ToList();
        }, cancellationToken);
    }

    private static IEnumerable<FileRecord> MergeCandidates(
        IEnumerable<FileRecord> rankedCandidates,
        IEnumerable<FileRecord> fallbackCandidates)
    {
        var seen = new HashSet<string>();
        foreach (var record in rankedCandidates)
        {
            if (seen.Add(record.Id))
                yield return record;
        }

        foreach (var record in fallbackCandidates
            .OrderByDescending(r => r.SavedAt ?? r.FirstSeenAt)
            .ThenBy(r => r.Filename))
        {
            if (seen.Add(record.Id))
                yield return record;
        }
    }

    private static bool MatchesContextFields(FileRecord record, string query)
    {
        return TextMatches(record.Reason, query) || TextMatches(record.Notes, query);
    }

    private static bool TextMatches(string? text, string query)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
            return false;

        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length > 1 && terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFindDefaultRecord(FileRecord record) =>
        record.Status is "contexted" or "legacy";

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
        "contexted" => "Has context",
        "legacy" => "Imported",
        "missing" => "Missing",
        _ => status,
    };
}
