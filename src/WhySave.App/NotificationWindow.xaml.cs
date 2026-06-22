using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WhySave.App;

public partial class NotificationWindow : Window
{
    private static readonly TimeSpan AutoDismissDelay = TimeSpan.FromSeconds(12);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly Action _onAddContext;
    private readonly Action _onLater;
    private readonly DispatcherTimer _autoDismiss;
    private bool _actionTaken;

    public NotificationWindow(string filename, Action onAddContext, Action onLater)
    {
        InitializeComponent();

        _onAddContext = onAddContext;
        _onLater = onLater;
        FilenameText.Text = filename;
        FilenameText.ToolTip = filename;

        _autoDismiss = new DispatcherTimer { Interval = AutoDismissDelay };
        _autoDismiss.Tick += (_, _) => Close();
        _autoDismiss.Start();

        // Keep the auto-dismiss from firing while the user is reading/hovering.
        MouseEnter += (_, _) => _autoDismiss.Stop();
        MouseLeave += (_, _) =>
        {
            _autoDismiss.Stop();
            _autoDismiss.Start();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Prevent the toast from stealing focus or appearing in Alt-Tab / the taskbar.
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void AddContextButton_Click(object sender, RoutedEventArgs e)
    {
        _actionTaken = true;
        _onAddContext();
        Close();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        _actionTaken = true;
        _onLater();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoDismiss.Stop();

        // Treating a timeout / manual dismiss the same as "Later": the item stays
        // pending and remains available in the Memory Inbox.
        if (!_actionTaken)
            _onLater();

        base.OnClosed(e);
    }
}
