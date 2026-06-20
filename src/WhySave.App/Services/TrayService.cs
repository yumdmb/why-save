using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;
using Serilog;

namespace WhySave.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly ILogger _logger;
    private TaskbarIcon? _trayIcon;

    public TrayService(ILogger logger) => _logger = logger;

    public void Start()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Why Save",
            IconSource = new GeneratedIconSource
            {
                Text = "Y",
                Foreground = Brushes.White,
                Background = Brushes.SteelBlue,
                FontSize = 16,
            },
            Visibility = Visibility.Visible,
        };

        var menu = new ContextMenu();
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);
        _trayIcon.ContextMenu = menu;

        _logger.Information("Tray icon started");
    }

    public void Stop()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visibility = Visibility.Hidden;
        }
        _logger.Information("Tray icon stopped");
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }
}
