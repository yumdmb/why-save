using System.Runtime.InteropServices;
using System.Windows.Interop;
using Serilog;
using WhySave.Native;

namespace WhySave.App.Services;

public sealed class SingleInstanceActivator : IDisposable
{
    public const string ActivationWindowTitle = "WhySaveActivationWindow";
    public const string ActivationMessageName = "WhySaveBringToFront";

    private readonly ILogger _logger;
    private HwndSource? _hwndSource;
    private readonly int _activationMessage;

    public HwndSource? HwndSource => _hwndSource;

    public SingleInstanceActivator(ILogger logger)
    {
        _logger = logger;
        _activationMessage = WindowMessage.RegisterWindowMessage(ActivationMessageName);
    }

    public event EventHandler? ActivateRequested;

    public void CreateListener()
    {
        if (_hwndSource is not null)
            return;

        var parameters = new HwndSourceParameters(ActivationWindowTitle)
        {
            WindowStyle = (int)WindowMessage.WS_OVERLAPPED,
            ExtendedWindowStyle = (int)(WindowMessage.WS_EX_NOACTIVATE | WindowMessage.WS_EX_TOOLWINDOW),
            ParentWindow = WindowMessage.HWND_MESSAGE,
            Width = 0,
            Height = 0,
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
        _logger.Information("Single-instance activation listener created on HWND {Hwnd}", _hwndSource.Handle);
    }

    public static bool TryActivateFirstInstance(ILogger logger)
    {
        var message = WindowMessage.RegisterWindowMessage(ActivationMessageName);
        var hwnd = WindowMessage.FindWindow(null, ActivationWindowTitle);
        if (hwnd == IntPtr.Zero)
        {
            logger.Information("No existing WhySave instance found to activate");
            return false;
        }

        logger.Information("Activating existing WhySave instance on HWND {Hwnd}", hwnd);
        WindowMessage.SendMessage(hwnd, message, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _activationMessage)
        {
            _logger.Information("Received activation request from second instance");
            ActivateRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WindowMessage.WM_HOTKEY)
        {
            // We do not register hotkeys on the activation window; ignore.
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _hwndSource?.Dispose();
        _hwndSource = null;
    }
}
