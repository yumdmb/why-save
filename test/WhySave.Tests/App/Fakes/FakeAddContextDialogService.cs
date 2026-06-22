using WhySave.App.Services;

namespace WhySave.Tests.App.Fakes;

public sealed class FakeAddContextDialogService : IAddContextDialogService
{
    public List<string> ShownFileIds { get; } = new();

    public void ShowAddContext(string fileId)
    {
        ShownFileIds.Add(fileId);
    }
}
