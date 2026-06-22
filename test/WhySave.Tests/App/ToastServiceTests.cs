using Serilog;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;
using WhySave.Tests.App.Fakes;
using WhySave.Tests.Storage;
using Xunit;

namespace WhySave.Tests.App;

public class ToastServiceTests : StorageTestBase
{
    private static ILogger CreateLogger() => new LoggerConfiguration().CreateLogger();

    [Fact]
    public void ShowPendingToast_ShowsToast_ForPendingRecord()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var dialog = new FakeAddContextDialogService();
        var presenter = new FakeToastPresenter();
        var toastService = new WhySave.App.Services.ToastService(CreateLogger(), repo, dialog, presenter);

        var record = CreatePendingRecord("file1");
        repo.Insert(record);

        toastService.ShowPendingToast(record.Id);

        Assert.Single(presenter.Shown);
        Assert.Equal(record.Id, presenter.Shown[0].Id);

        var updated = repo.GetById(record.Id);
        Assert.NotNull(updated);
        Assert.True(updated.LastPromptedAt.HasValue);
    }

    [Fact]
    public void ShowPendingToast_SkipsNonPendingRecord()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var dialog = new FakeAddContextDialogService();
        var presenter = new FakeToastPresenter();
        var toastService = new WhySave.App.Services.ToastService(CreateLogger(), repo, dialog, presenter);

        var record = CreatePendingRecord("file2");
        record.Status = "contexted";
        repo.Insert(record);

        toastService.ShowPendingToast(record.Id);

        Assert.Empty(presenter.Shown);
    }

    [Fact]
    public void ShowPendingToast_DebouncesWithinTenMinutes()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var dialog = new FakeAddContextDialogService();
        var presenter = new FakeToastPresenter();
        var toastService = new WhySave.App.Services.ToastService(CreateLogger(), repo, dialog, presenter);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var record = CreatePendingRecord("file3");
        record.LastPromptedAt = nowMs - (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
        repo.Insert(record);

        toastService.ShowPendingToast(record.Id);

        Assert.Empty(presenter.Shown);
    }

    [Fact]
    public void ShowPendingToast_AllowsReToastAfterTenMinutes()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var dialog = new FakeAddContextDialogService();
        var presenter = new FakeToastPresenter();
        var toastService = new WhySave.App.Services.ToastService(CreateLogger(), repo, dialog, presenter);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var record = CreatePendingRecord("file4");
        record.LastPromptedAt = nowMs - (long)TimeSpan.FromMinutes(11).TotalMilliseconds;
        repo.Insert(record);

        toastService.ShowPendingToast(record.Id);

        Assert.Single(presenter.Shown);
    }

    [Fact]
    public void ShowPendingToast_AddContextCallback_OpensDialogForFile()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var dialog = new FakeAddContextDialogService();
        var presenter = new FakeToastPresenter();
        var toastService = new WhySave.App.Services.ToastService(CreateLogger(), repo, dialog, presenter);

        var record = CreatePendingRecord("file5");
        repo.Insert(record);

        toastService.ShowPendingToast(record.Id);

        Assert.NotNull(presenter.LastOnAddContext);
        Assert.Empty(dialog.ShownFileIds);

        // Simulate the user clicking "Add Context" on the notification.
        presenter.LastOnAddContext!.Invoke();

        Assert.Single(dialog.ShownFileIds);
        Assert.Equal(record.Id, dialog.ShownFileIds[0]);
    }

    private static FileRecord CreatePendingRecord(string id)
    {
        return new FileRecord
        {
            Id = id,
            Path = $"C:\\temp\\{id}.pdf",
            Filename = $"{id}.pdf",
            Ext = ".pdf",
            SizeBytes = 1234,
            Status = "pending",
            FirstSeenAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }
}
