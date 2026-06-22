using System.IO;
using System.Security.Cryptography;
using WhySave.Core;
using WhySave.Crypto;
using WhySave.Storage;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.Tests.Core;

public class SearchServiceTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly string _tempPath;
    private readonly byte[] _key;
    private readonly FilesRepository _repo;
    private readonly SearchService _searchService;

    public SearchServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "WhySaveSearchTestDb_" + Guid.NewGuid().ToString("N") + ".db");
        _connection = SqliteConnectionFactory.Create(_tempPath);
        new DatabaseMigrator(_connection).MigrateAsync();
        _key = RandomNumberGenerator.GetBytes(AesGcmCrypto.KeySize);
        _repo = new FilesRepository(_connection, _key);
        _searchService = new SearchService(_repo);
    }

    public void Dispose()
    {
        _connection.Dispose();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _tempPath + ext;
            if (File.Exists(p))
            {
                try { File.Delete(p); } catch { }
            }
        }
    }

    private static FileRecord NewRecord(
        string id, string path, string filename, string? project = null,
        string? url = null, string status = "contexted", string? reason = null) =>
        new()
        {
            Id = id,
            Path = path,
            Filename = filename,
            Ext = Path.GetExtension(filename),
            SizeBytes = 4096,
            Status = status,
            Project = project,
            Url = url,
            Reason = reason,
            FirstSeenAt = 5000,
            SavedAt = 5000,
            CreatedAt = 1000,
            UpdatedAt = 1000,
        };

    [Fact]
    public async Task Keyword_Matches_Filename()
    {
        _repo.Insert(NewRecord("s1", "/p/AI_Paper.pdf", "AI_Paper.pdf", project: "ML-course"));
        _repo.Insert(NewRecord("s2", "/p/notes.txt", "notes.txt", project: "general"));

        var results = await _searchService.SearchAsync("AI_Paper");
        Assert.Single(results);
        Assert.Equal("s1", results[0].FileId);
        Assert.Equal("AI_Paper.pdf", results[0].Filename);
    }

    [Fact]
    public async Task Keyword_Matches_Project()
    {
        _repo.Insert(NewRecord("s3", "/p/AI_Paper.pdf", "AI_Paper.pdf", project: "ML-course"));
        _repo.Insert(NewRecord("s4", "/p/other.txt", "other.txt", project: "general"));

        var results = await _searchService.SearchAsync("ML-course");
        Assert.Single(results);
        Assert.Equal("s3", results[0].FileId);
        Assert.Equal("ML-course", results[0].Project);
    }

    [Fact]
    public async Task Reason_Scope_Finds_Match_Among_Candidates()
    {
        _repo.Insert(NewRecord("s5", "/p/AI_Paper.pdf", "AI_Paper.pdf",
            project: "ML-course", reason: "For the transformers lecture"));
        _repo.Insert(NewRecord("s6", "/p/AI_Notes.pdf", "AI_Notes.pdf",
            project: "ML-course", reason: "For the meeting notes"));

        var results = await _searchService.SearchAsync("AI reason:transformers");
        Assert.Single(results);
        Assert.Equal("s5", results[0].FileId);
        Assert.Contains("transformers", results[0].ReasonSnippet);
    }

    [Fact]
    public async Task Reason_Scope_With_No_Fts_Query_Filters_All_Rows()
    {
        _repo.Insert(NewRecord("s7", "/p/AI_Paper.pdf", "AI_Paper.pdf",
            project: "ML-course", reason: "For the transformers lecture"));
        _repo.Insert(NewRecord("s8", "/p/notes.txt", "notes.txt",
            project: "general", reason: "For the meeting"));

        var results = await _searchService.SearchAsync("reason:transformers");
        Assert.Single(results);
        Assert.Equal("s7", results[0].FileId);
    }

    [Fact]
    public async Task Reason_Scope_Finds_No_Match_Among_Candidates()
    {
        _repo.Insert(NewRecord("s9", "/p/AI_Paper.pdf", "AI_Paper.pdf",
            project: "ML-course", reason: "For the transformers lecture"));

        var results = await _searchService.SearchAsync("AI reason:meeting");
        Assert.Empty(results);
    }

    [Fact]
    public async Task No_Results_Returns_Empty()
    {
        _repo.Insert(NewRecord("s10", "/p/file.pdf", "file.pdf"));

        var results = await _searchService.SearchAsync("nonexistentterm");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Empty_Query_Returns_Empty()
    {
        _repo.Insert(NewRecord("s11", "/p/file.pdf", "file.pdf"));

        var results = await _searchService.SearchAsync("");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Results_Contain_Required_Fields()
    {
        _repo.Insert(NewRecord("s12", "/p/AI_Paper.pdf", "AI_Paper.pdf",
            project: "ML-course", reason: "For the transformers lecture"));

        var results = await _searchService.SearchAsync("AI_Paper");
        Assert.Single(results);
        var r = results[0];
        Assert.Equal("s12", r.FileId);
        Assert.Equal("AI_Paper.pdf", r.Filename);
        Assert.Equal("/p/AI_Paper.pdf", r.Path);
        Assert.Equal("ML-course", r.Project);
        Assert.Equal("Contexted", r.StatusBadge);
        Assert.NotNull(r.SavedAt);
        Assert.NotNull(r.ReasonSnippet);
    }

    [Fact]
    public async Task Reason_Snippet_Is_Truncated_When_Too_Long()
    {
        var longReason = new string('x', 200);
        _repo.Insert(NewRecord("s13", "/p/long.pdf", "long.pdf", reason: longReason));

        var results = await _searchService.SearchAsync("long");
        Assert.Single(results);
        Assert.NotNull(results[0].ReasonSnippet);
        Assert.True(results[0].ReasonSnippet!.Length <= SearchService.ReasonSnippetLength + 1);
        Assert.EndsWith("…", results[0].ReasonSnippet);
    }

    [Fact]
    public async Task Reason_Snippet_Is_Null_When_No_Reason()
    {
        _repo.Insert(NewRecord("s14", "/p/noreason.pdf", "noreason.pdf"));

        var results = await _searchService.SearchAsync("noreason");
        Assert.Single(results);
        Assert.Null(results[0].ReasonSnippet);
    }

    [Fact]
    public async Task Results_Limited_To_200_Candidates()
    {
        for (var i = 0; i < 250; i++)
            _repo.Insert(NewRecord($"lim{i}", $"/p/report_{i}.pdf", $"report_{i}.pdf"));

        var results = await _searchService.SearchAsync("report");
        Assert.Equal(200, results.Count);
    }

    [Theory]
    [InlineData("reason:transformers", "transformers", "")]
    [InlineData("AI_Paper", null, "AI_Paper")]
    [InlineData("AI_Paper reason:transformers", "transformers", "AI_Paper")]
    [InlineData("reason:\"for the meeting\"", "for the meeting", "")]
    [InlineData("AI_Paper reason:\"for the meeting\"", "for the meeting", "AI_Paper")]
    public void ParseQuery_Extracts_Reason_Term_And_Fts_Query(string input, string? expectedReason, string expectedFts)
    {
        var (reasonTerm, ftsQuery) = SearchService.ParseQuery(input);
        Assert.Equal(expectedReason, reasonTerm);
        Assert.Equal(expectedFts, ftsQuery);
    }

    [Fact]
    public void ParseQuery_No_Reason_Scope_Returns_Full_Query_As_Fts()
    {
        var (reasonTerm, ftsQuery) = SearchService.ParseQuery("just a keyword query");
        Assert.Null(reasonTerm);
        Assert.Equal("just a keyword query", ftsQuery);
    }

    [Fact]
    public void Snippet_Returns_Null_For_Null_Reason()
    {
        Assert.Null(SearchService.Snippet(null));
    }

    [Fact]
    public void Snippet_Returns_Full_Reason_When_Short()
    {
        Assert.Equal("short", SearchService.Snippet("short"));
    }

    [Fact]
    public void MarkOpenedViaApp_Updates_Timestamp()
    {
        _repo.Insert(NewRecord("s15", "/p/file.pdf", "file.pdf"));
        _repo.MarkOpenedViaApp("s15");

        var fetched = _repo.GetById("s15");
        Assert.NotNull(fetched);
        Assert.NotNull(fetched!.LastOpenedViaAppAt);
        Assert.True(fetched.LastOpenedViaAppAt.Value > 0);
    }
}
