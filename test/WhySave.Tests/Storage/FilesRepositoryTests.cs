using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.Tests.Storage;

public class FilesRepositoryTests : StorageTestBase
{
    private static FileRecord NewRecord(
        string id, string path = "/p/file.pdf", string filename = "file.pdf",
        long? volumeSerial = null, long? ntfsFileId = null, string? sha256 = null,
        string status = "pending", string? project = null, string? url = null) =>
        new()
        {
            Id = id,
            Path = path,
            Filename = filename,
            Ext = ".pdf",
            SizeBytes = 4096,
            VolumeSerial = volumeSerial,
            NtfsFileId = ntfsFileId,
            Sha256 = sha256,
            Status = status,
            Project = project,
            Url = url,
            FirstSeenAt = 5000,
            SavedAt = 5000,
            CreatedAt = 1000,
            UpdatedAt = 1000,
        };

    [Fact]
    public void Insert_And_GetById_RoundTrip()
    {
        var repo = new FilesRepository(Connection);
        var record = NewRecord("r1", project: "ML-course", url: "https://example.com");

        repo.Insert(record);

        var fetched = repo.GetById("r1");
        Assert.NotNull(fetched);
        Assert.Equal("r1", fetched!.Id);
        Assert.Equal("/p/file.pdf", fetched.Path);
        Assert.Equal("file.pdf", fetched.Filename);
        Assert.Equal(".pdf", fetched.Ext);
        Assert.Equal(4096, fetched.SizeBytes);
        Assert.Equal("ML-course", fetched.Project);
        Assert.Equal("https://example.com", fetched.Url);
        Assert.Equal("pending", fetched.Status);
        Assert.Equal(5000, fetched.FirstSeenAt);
    }

    [Fact]
    public void GetById_Returns_Null_When_Not_Found()
    {
        var repo = new FilesRepository(Connection);
        Assert.Null(repo.GetById("nonexistent"));
    }

    [Fact]
    public void GetByPath_Returns_Matching_Record()
    {
        var repo = new FilesRepository(Connection);
        repo.Insert(NewRecord("r2", path: "/downloads/report.docx", filename: "report.docx"));

        var fetched = repo.GetByPath("/downloads/report.docx");
        Assert.NotNull(fetched);
        Assert.Equal("r2", fetched!.Id);
    }

    [Fact]
    public void GetByNtfsId_Returns_Matching_Record()
    {
        var repo = new FilesRepository(Connection);
        repo.Insert(NewRecord("r3", volumeSerial: 12345, ntfsFileId: 67890));

        var fetched = repo.GetByNtfsId(12345, 67890);
        Assert.NotNull(fetched);
        Assert.Equal("r3", fetched!.Id);
    }

    [Fact]
    public void GetByNtfsId_Returns_Null_When_No_Match()
    {
        var repo = new FilesRepository(Connection);
        Assert.Null(repo.GetByNtfsId(11111, 22222));
    }

    [Fact]
    public void GetBySha256_Returns_Matching_Record()
    {
        var repo = new FilesRepository(Connection);
        repo.Insert(NewRecord("r4", sha256: "abc123hash"));

        var fetched = repo.GetBySha256("abc123hash");
        Assert.NotNull(fetched);
        Assert.Equal("r4", fetched!.Id);
    }

    [Fact]
    public void ListByStatus_Returns_Only_Matching_Status()
    {
        var repo = new FilesRepository(Connection);
        repo.Insert(NewRecord("p1", status: "pending"));
        repo.Insert(NewRecord("p2", status: "pending"));
        repo.Insert(NewRecord("l1", status: "legacy"));
        repo.Insert(NewRecord("c1", status: "contexted"));

        var pending = repo.ListByStatus("pending").ToList();
        Assert.Equal(2, pending.Count);
        Assert.All(pending, r => Assert.Equal("pending", r.Status));

        var legacy = repo.ListByStatus("legacy").ToList();
        Assert.Single(legacy);
        Assert.Equal("l1", legacy[0].Id);
    }

    [Fact]
    public void Update_Modifies_Existing_Record()
    {
        var repo = new FilesRepository(Connection);
        var record = NewRecord("r5", status: "pending");
        repo.Insert(record);

        record.Status = "contexted";
        record.Project = "updated-project";
        record.UpdatedAt = 9999;
        repo.Update(record);

        var fetched = repo.GetById("r5");
        Assert.NotNull(fetched);
        Assert.Equal("contexted", fetched!.Status);
        Assert.Equal("updated-project", fetched.Project);
        Assert.Equal(9999, fetched.UpdatedAt);
    }

    [Fact]
    public void Update_Preserves_Cipher_Blobs()
    {
        var repo = new FilesRepository(Connection);
        var record = NewRecord("r6");
        repo.Insert(record);

        record.ReasonCipher = new byte[] { 1, 2, 3, 4, 5 };
        record.NotesCipher = new byte[] { 9, 8, 7 };
        repo.Update(record);

        var fetched = repo.GetById("r6");
        Assert.NotNull(fetched);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, fetched!.ReasonCipher);
        Assert.Equal(new byte[] { 9, 8, 7 }, fetched.NotesCipher);
    }

    [Fact]
    public void SearchFts_Returns_Ranked_Matches()
    {
        var repo = new FilesRepository(Connection);
        repo.Insert(NewRecord("s1", path: "/p/AI_Paper.pdf", filename: "AI_Paper.pdf", project: "ML-course"));
        repo.Insert(NewRecord("s2", path: "/p/notes.txt", filename: "notes.txt", project: "general"));
        repo.Insert(NewRecord("s3", path: "/p/ml-notes.pdf", filename: "ml-notes.pdf", project: "ML-course"));

        var results = repo.SearchFts("AI_Paper").ToList();
        Assert.Single(results);
        Assert.Equal("s1", results[0].Id);
    }

    [Fact]
    public void SearchFts_Matches_By_Project()
    {
        var repo = new FilesRepository(Connection);
        repo.Insert(NewRecord("s4", path: "/p/AI_Paper.pdf", filename: "AI_Paper.pdf", project: "ML-course"));
        repo.Insert(NewRecord("s5", path: "/p/other.txt", filename: "other.txt", project: "general"));

        var results = repo.SearchFts("ML-course").ToList();
        Assert.Single(results);
        Assert.Equal("s4", results[0].Id);
    }

    [Fact]
    public void SearchFts_Returns_Empty_When_No_Match()
    {
        var repo = new FilesRepository(Connection);
        repo.Insert(NewRecord("s6", filename: "file.pdf"));

        var results = repo.SearchFts("nonexistentterm").ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Delete_Removes_Record()
    {
        var repo = new FilesRepository(Connection);
        repo.Insert(NewRecord("del1"));

        repo.Delete("del1");

        Assert.Null(repo.GetById("del1"));
    }

    [Fact]
    public void SearchFts_Respects_Limit()
    {
        var repo = new FilesRepository(Connection);
        for (var i = 0; i < 10; i++)
            repo.Insert(NewRecord($"lim{i}", path: $"/p/report_{i}.pdf", filename: $"report_{i}.pdf"));

        var results = repo.SearchFts("report", limit: 3).ToList();
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void GetRecentProjects_Returns_Distinct_NonEmpty_Projects_Ordered_By_Recent()
    {
        var repo = new FilesRepository(Connection);
        var ml = NewRecord("proj1", project: "ML-course", url: null);
        var general = NewRecord("proj2", project: "general", url: null);
        repo.Insert(ml);
        repo.Insert(general);
        repo.Insert(NewRecord("proj3", project: "ML-course", url: null));
        repo.Insert(NewRecord("proj4", project: null, url: null));
        repo.Insert(NewRecord("proj5", project: "", url: null));

        ml.UpdatedAt = 9999;
        repo.Update(ml);

        var projects = repo.GetRecentProjects(10).ToList();
        Assert.Equal(2, projects.Count);
        Assert.Equal("ML-course", projects[0]);
        Assert.Equal("general", projects[1]);
    }
}
