using WhySave.Storage.Models;

namespace WhySave.App.Services;

public interface IToastPresenter
{
    void Show(FileRecord record, string arguments, string laterArguments);
}
