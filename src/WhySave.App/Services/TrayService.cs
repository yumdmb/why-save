using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using Serilog;
using WhySave.App.ViewModels;
using WhySave.Core;
using WhySave.Storage.Repositories;

namespace WhySave.App.Services;

public sealed class TrayService : IDisposable
{
    private static readonly Uri TrayIconUri = new("pack://application:,,,/Assets/WhySave.ico", UriKind.Absolute);

    private readonly MainWindow _mainWindow;
    private readonly FilesRepository _filesRepository;
    private readonly FileWatchService _fileWatchService;
    private readonly SettingsService _settingsService;
    private readonly GlobalHotKeyService _hotKeyService;
    private readonly JunkFilter _junkFilter;
    private readonly DataManagementService _dataManagement;
    private readonly ILogger _logger;
    private TaskbarIcon? _trayIcon;
    private MenuItem? _inboxMenuItem;
    private DispatcherTimer? _refreshTimer;
    private bool _isPaused;

    public TrayService(
        MainWindow mainWindow,
        FilesRepository filesRepository,
        FileWatchService fileWatchService,
        SettingsService settingsService,
        GlobalHotKeyService hotKeyService,
        JunkFilter junkFilter,
        DataManagementService dataManagement,
        ILogger logger)
    {
        _mainWindow = mainWindow;
        _filesRepository = filesRepository;
        _fileWatchService = fileWatchService;
        _settingsService = settingsService;
        _hotKeyService = hotKeyService;
        _junkFilter = junkFilter;
        _dataManagement = dataManagement;
        _logger = logger;
    }

    public void Start()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Why Save",
            IconSource = BitmapFrame.Create(TrayIconUri),
            Visibility = Visibility.Visible,
        };

        _trayIcon.ForceCreate();

        _trayIcon.TrayLeftMouseDown += (_, _) => ShowTab(MainTab.Find);

        var menu = new ContextMenu();

        var findItem = new MenuItem { Header = "Find" };
        findItem.Click += (_, _) => ShowTab(MainTab.Find);
        menu.Items.Add(findItem);

        _inboxMenuItem = new MenuItem { Header = "Memory Inbox" };
        _inboxMenuItem.Click += (_, _) => ShowTab(MainTab.Inbox);
        menu.Items.Add(_inboxMenuItem);

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        var pauseItem = new MenuItem { Header = "Pause watching" };
        pauseItem.Click += (_, _) => TogglePause();
        menu.Items.Add(pauseItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(5),
            DispatcherPriority.Background,
            (_, _) => RefreshPendingCount(),
            Application.Current.Dispatcher);
        _refreshTimer.Start();

        RefreshPendingCount();

        _logger.Information("Tray icon started");
    }

    public void Stop()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;

        if (_trayIcon is not null)
        {
            _trayIcon.Visibility = Visibility.Hidden;
        }

        _logger.Information("Tray icon stopped");
    }

    public void ShowTab(MainTab tab)
    {
        _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
        {
            _mainWindow.SelectTab(tab);
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();
        }));
    }

    public void RefreshPendingCount()
    {
        if (_inboxMenuItem is null || _trayIcon is null)
            return;

        var count = _filesRepository.ListByStatus("pending").Count();
        var badge = count > 0 ? $"🔴 {count} files need context" : "Memory Inbox";

        _trayIcon.ToolTipText = count > 0
            ? $"Why Save - {count} pending"
            : "Why Save";

        Application.Current.Dispatcher.Invoke(() =>
        {
            _inboxMenuItem.Header = badge;
        });
    }

    private void OpenSettings()
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            var existing = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
            if (existing is not null)
            {
                existing.Activate();
                return;
            }

            var settingsWindow = new SettingsWindow(
                _settingsService,
                _fileWatchService,
                _hotKeyService,
                _junkFilter,
                _dataManagement,
                _logger);
            settingsWindow.Show();
        }));
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        _logger.Information("File watching paused = {Paused}", _isPaused);

        if (_isPaused)
        {
            foreach (var folder in _fileWatchService.WatchedFolders.ToList())
                _fileWatchService.RemoveFolder(folder);
        }
        else
        {
            var folders = _settingsService.Current.WatchedFolders.Any()
                ? _settingsService.Current.WatchedFolders
                : new List<string> { RescanService.GetDefaultDownloadsPath() };

            _fileWatchService.Start(folders);
        }
    }

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _trayIcon?.Dispose();
    }
}
