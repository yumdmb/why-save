using Serilog;
using WhySave.App.Services;
using WhySave.App.ViewModels;
using WhySave.Storage.Repositories;
using WhySave.Tests.App.Fakes;
using WhySave.Tests.Storage;
using Xunit;

namespace WhySave.Tests.App;

public class InboxViewModelTests : StorageTestBase
{
    private static ILogger CreateLogger() => new LoggerConfiguration().CreateLogger();

    [Fact]
    public void Refresh_Populates_Pending_Items_And_Tracks_Empty_State()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var dialog = new FakeAddContextDialogService();
        var vm = new InboxViewModel(repo, dialog, CreateLogger());

        Assert.True(vm.IsEmpty);

        repo.Insert(new WhySave.Storage.Models.FileRecord
        {
            Id = "inbox1",
            Path = "C:\\temp\\inbox1.pdf",
            Filename = "inbox1.pdf",
            Ext = ".pdf",
            SizeBytes = 1234,
            Status = "pending",
            FirstSeenAt = 1000,
            SavedAt = 1000,
            CreatedAt = 1000,
            UpdatedAt = 1000,
        });

        vm.Refresh();

        Assert.Single(vm.Items);
        Assert.False(vm.IsEmpty);
        Assert.Equal(1, vm.PendingCount);
    }

    [Fact]
    public void AddWhyCommand_Opens_Dialog_For_Row()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var dialog = new FakeAddContextDialogService();
        var vm = new InboxViewModel(repo, dialog, CreateLogger());

        repo.Insert(new WhySave.Storage.Models.FileRecord
        {
            Id = "inbox2",
            Path = "C:\\temp\\inbox2.pdf",
            Filename = "inbox2.pdf",
            Ext = ".pdf",
            SizeBytes = 1234,
            Status = "pending",
            FirstSeenAt = 1000,
            SavedAt = 1000,
            CreatedAt = 1000,
            UpdatedAt = 1000,
        });

        vm.Refresh();
        var row = vm.Items[0];

        vm.AddWhyCommand.Execute(row);

        Assert.Single(dialog.ShownFileIds);
        Assert.Equal("inbox2", dialog.ShownFileIds[0]);
    }

    [Fact]
    public void DismissSelectedToLegacyCommand_Transitions_Pending_To_Legacy()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var dialog = new FakeAddContextDialogService();
        var vm = new InboxViewModel(repo, dialog, CreateLogger());

        repo.Insert(new WhySave.Storage.Models.FileRecord
        {
            Id = "inbox3",
            Path = "C:\\temp\\inbox3.pdf",
            Filename = "inbox3.pdf",
            Ext = ".pdf",
            SizeBytes = 1234,
            Status = "pending",
            FirstSeenAt = 1000,
            SavedAt = 1000,
            CreatedAt = 1000,
            UpdatedAt = 1000,
        });

        vm.Refresh();
        vm.SelectedItems.Add(vm.Items[0]);
        vm.DismissSelectedToLegacyCommand.Execute(null);

        Assert.Empty(vm.Items);
        Assert.True(vm.IsEmpty);
        var record = repo.GetById("inbox3");
        Assert.NotNull(record);
        Assert.Equal("legacy", record!.Status);
    }

    [Fact]
    public void DeleteSelectedCommand_Removes_Records()
    {
        var repo = new FilesRepository(Connection, cryptoKey: null);
        var dialog = new FakeAddContextDialogService();
        var vm = new InboxViewModel(repo, dialog, CreateLogger());

        repo.Insert(new WhySave.Storage.Models.FileRecord
        {
            Id = "inbox4",
            Path = "C:\\temp\\inbox4.pdf",
            Filename = "inbox4.pdf",
            Ext = ".pdf",
            SizeBytes = 1234,
            Status = "pending",
            FirstSeenAt = 1000,
            SavedAt = 1000,
            CreatedAt = 1000,
            UpdatedAt = 1000,
        });

        vm.Refresh();
        vm.SelectedItems.Add(vm.Items[0]);
        vm.DeleteSelectedCommand.Execute(null);

        Assert.Empty(vm.Items);
        Assert.Null(repo.GetById("inbox4"));
    }
}
