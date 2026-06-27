using Serilog;
using WhySave.App.ViewModels;
using WhySave.Core;
using WhySave.Crypto;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;
using WhySave.Tests.App.Fakes;
using WhySave.Tests.Storage;
using Xunit;

namespace WhySave.Tests.App;

public class SearchViewModelTests : StorageTestBase
{
    private static ILogger CreateLogger() => new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task RefreshAsync_Reloads_Imported_Row_After_Context_Is_Added()
    {
        var key = new byte[AesGcmCrypto.KeySize];
        Random.Shared.NextBytes(key);
        var repo = new FilesRepository(Connection, key);
        var viewModel = new SearchViewModel(
            new SearchService(repo),
            repo,
            new FakeAddContextDialogService(),
            CreateLogger());

        var record = NewRecord("find1", status: "legacy");
        repo.Insert(record);

        await viewModel.RefreshAsync();

        Assert.Single(viewModel.Results);
        Assert.Equal("Imported", viewModel.Results[0].StatusBadge);
        Assert.Null(viewModel.Results[0].ReasonSnippet);

        record.Status = "contexted";
        record.Reason = "For the UX review";
        record.LastResolvedAt = 2000;
        repo.Update(record);

        await viewModel.RefreshAsync();

        Assert.Single(viewModel.Results);
        Assert.Equal("Has context", viewModel.Results[0].StatusBadge);
        Assert.Equal("For the UX review", viewModel.Results[0].ReasonSnippet);
    }

    private static FileRecord NewRecord(string id, string status) =>
        new()
        {
            Id = id,
            Path = $"C:\\temp\\{id}.pdf",
            Filename = $"{id}.pdf",
            Ext = ".pdf",
            SizeBytes = 1234,
            Status = status,
            FirstSeenAt = 1000,
            SavedAt = 1000,
            CreatedAt = 1000,
            UpdatedAt = 1000,
        };
}
