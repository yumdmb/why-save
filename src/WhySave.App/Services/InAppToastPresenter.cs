using System.Windows;
using WhySave.Storage.Models;

namespace WhySave.App.Services;

/// <summary>
/// Presents pending-file notifications as in-app WPF windows stacked in the bottom-right
/// corner of the working area. This runs entirely in-process, avoiding the unreliable COM
/// activation path of OS toasts for unpackaged desktop apps.
/// </summary>
public sealed class InAppToastPresenter : IToastPresenter
{
    private const double Gap = 8;
    private readonly List<NotificationWindow> _open = new();

    public void Show(FileRecord record, Action onAddContext, Action onLater)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        dispatcher.BeginInvoke(new Action(() =>
        {
            var window = new NotificationWindow(record.Filename, onAddContext, onLater);
            window.Closed += (_, _) =>
            {
                _open.Remove(window);
                Reflow();
            };

            _open.Add(window);
            window.Show();

            // Position once the content has been measured so we know the height.
            window.Dispatcher.BeginInvoke(new Action(Reflow),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }));
    }

    private void Reflow()
    {
        var workArea = SystemParameters.WorkArea;
        var bottom = workArea.Bottom;

        // Newest window sits at the bottom; older ones stack upward.
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            var window = _open[i];
            var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            window.Left = workArea.Right - window.Width;
            window.Top = bottom - height;
            bottom -= height + Gap;
        }
    }
}
