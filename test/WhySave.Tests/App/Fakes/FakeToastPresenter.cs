using WhySave.App.Services;
using WhySave.Storage.Models;

namespace WhySave.Tests.App.Fakes;

public sealed class FakeToastPresenter : IToastPresenter
{
    public List<FileRecord> Shown { get; } = new();
    public Action? LastOnAddContext { get; private set; }
    public Action? LastOnLater { get; private set; }

    public void Show(FileRecord record, Action onAddContext, Action onLater)
    {
        Shown.Add(record);
        LastOnAddContext = onAddContext;
        LastOnLater = onLater;
    }
}
