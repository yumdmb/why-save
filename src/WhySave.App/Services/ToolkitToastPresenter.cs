using Microsoft.Toolkit.Uwp.Notifications;
using WhySave.Storage.Models;

namespace WhySave.App.Services;

public sealed class ToolkitToastPresenter : IToastPresenter
{
    public void Show(FileRecord record, string arguments, string laterArguments)
    {
        new ToastContentBuilder()
            .AddText(record.Filename)
            .AddText("Why did you save this?")
            .AddButton(new ToastButton("Add Context", arguments))
            .AddButton(new ToastButton("Later", laterArguments)
                .SetBackgroundActivation())
            .Show();
    }
}
