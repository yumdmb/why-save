using WhySave.App.Services;
using WhySave.Storage.Models;

namespace WhySave.Tests.App.Fakes;

public sealed class FakeToastPresenter : IToastPresenter
{
    public List<FileRecord> Shown { get; } = new();

    public void Show(FileRecord record, string arguments, string laterArguments)
    {
        Shown.Add(record);
    }
}
