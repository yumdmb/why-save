using Serilog;
using WhySave.App.ViewModels;
using WhySave.Crypto;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;
using WhySave.Tests.Storage;
using Xunit;

namespace WhySave.Tests.App;

public class AddContextViewModelTests : StorageTestBase
{
    private static ILogger CreateLogger() => new LoggerConfiguration().CreateLogger();

    [Fact]
    public void Save_EncryptsReasonAndNotes_AndSetsContexted()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);
        var repo = new FilesRepository(Connection, key);
        var record = CreatePendingRecord("ctx1");
        repo.Insert(record);

        var viewModel = new AddContextViewModel(
            record,
            repo,
            CreateLogger(),
            Array.Empty<string>()
        )
        {
            Reason = "For the transformers lecture",
            Project = "ML-course",
            SourceUrl = "https://example.com/paper",
            Notes = "see slide 12",
        };

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.Saved);

        var updated = repo.GetById(record.Id);
        Assert.NotNull(updated);
        Assert.Equal("contexted", updated.Status);
        Assert.Equal("ML-course", updated.Project);
        Assert.Equal("https://example.com/paper", updated.Url);
        Assert.Equal("For the transformers lecture", updated.Reason);
        Assert.Equal("see slide 12", updated.Notes);
        Assert.True(updated.LastResolvedAt.HasValue);
        Assert.NotNull(updated.ReasonCipher);
        Assert.NotNull(updated.NotesCipher);
    }

    [Fact]
    public void Save_WithEmptyOptionalFields_ClearsThem()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var record = CreatePendingRecord("ctx2");
        record.Project = "Old project";
        record.Url = "https://old.example.com";
        repo.Insert(record);

        var viewModel = new AddContextViewModel(
            record,
            repo,
            CreateLogger(),
            Array.Empty<string>()
        )
        {
            Reason = "Reason only",
            Project = "   ",
            SourceUrl = "",
            Notes = "",
        };

        viewModel.SaveCommand.Execute(null);

        var updated = repo.GetById(record.Id);
        Assert.NotNull(updated);
        Assert.Equal("contexted", updated.Status);
        Assert.Null(updated.Project);
        Assert.Null(updated.Url);
        Assert.Null(updated.Notes);
    }

    private static FileRecord CreatePendingRecord(string id)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new FileRecord
        {
            Id = id,
            Path = $"C:\\temp\\{id}.pdf",
            Filename = $"{id}.pdf",
            Ext = ".pdf",
            SizeBytes = 1234,
            Status = "pending",
            FirstSeenAt = nowMs,
            SavedAt = nowMs,
            CreatedAt = nowMs,
            UpdatedAt = nowMs,
        };
    }
}
