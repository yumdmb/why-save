using WhySave.Storage.Models;

namespace WhySave.App.Services;

public interface IToastPresenter
{
    /// <summary>
    /// Shows a notification for a pending file. <paramref name="onAddContext"/> is invoked when the
    /// user chooses to add context; <paramref name="onLater"/> when they dismiss or it times out.
    /// </summary>
    void Show(FileRecord record, Action onAddContext, Action onLater);
}
