using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WhySave.App.Services;
using WhySave.App.ViewModels;
using WhySave.Core;
using WhySave.Crypto;
using WhySave.Storage;
using WhySave.Storage.Repositories;

namespace WhySave.App.Infrastructure;

public sealed class AppBootstrapper : IDisposable
{
    private readonly IHost _host;
    private readonly ILogger _logger;
    private readonly SqliteConnection _connection;
    private readonly DpapiKeyStore _keyStore;
    private readonly SingleInstanceActivator _activator;
    private readonly GlobalHotKeyService _hotKeyService;
    private readonly SettingsService _settingsService;
    private UpdateService? _updateService;
    private Timer? _hourlyRescanTimer;
    private bool _stopped;
    private bool _disposed;

    public AppBootstrapper()
    {
        _logger = LoggerSetup.CreateLogger();
        Log.Logger = _logger;

        AppPaths.EnsureAppDataDirExists();

        _settingsService = new SettingsService(AppPaths.SettingsPath, _logger);
        LoggerSetup.SetVerbose(_settingsService.Current.LogLevelVerbose);

        _connection = SqliteConnectionFactory.Create(AppPaths.DatabasePath);
        new DatabaseMigrator(_connection).MigrateAsync();

        _keyStore = new DpapiKeyStore(AppPaths.KeyPath);
        var key = _keyStore.GetOrCreateKey();

        _activator = new SingleInstanceActivator(_logger);
        _hotKeyService = new GlobalHotKeyService(_logger);

        _host = new HostBuilder()
            .UseSerilog(_logger, dispose: true)
            .ConfigureServices(services =>
            {
                services.AddSingleton(_connection);
                services.AddSingleton(_keyStore);
                services.AddSingleton(_settingsService);
                services.AddSingleton(s => new FilesRepository(_connection, key));
                services.AddSingleton(s => new AppMetaRepository(_connection));
                services.AddSingleton<IIdentityResolver>(s => new IdentityResolver(s.GetRequiredService<FilesRepository>()));
                services.AddSingleton<IFileIngester>(s => new FileIngester(
                    s.GetRequiredService<IIdentityResolver>(),
                    s.GetRequiredService<FilesRepository>(),
                    s.GetRequiredService<AppMetaRepository>()));
                services.AddSingleton(s =>
                {
                    var settings = _settingsService.Current;
                    return new JunkFilter(new JunkFilterRules
                    {
                        BlockGlobs = settings.JunkRules.BlockGlobs.Any()
                            ? settings.JunkRules.BlockGlobs
                            : JunkFilter.DefaultBlockGlobs,
                        AllowGlobs = settings.JunkRules.AllowGlobs,
                        MinSizeBytes = settings.JunkRules.MinSizeBytes,
                    });
                });
                services.AddSingleton(Channel.CreateUnbounded<FileEvent>());
                services.AddSingleton<IFileEventSink>(s => new ChannelFileEventSink(s.GetRequiredService<Channel<FileEvent>>()));
                services.AddSingleton(s => new FileWatchService(
                    s.GetRequiredService<IFileEventSink>(),
                    (msg, ex) => _logger.Warning(ex, "{WatcherMessage}", msg)));
                services.AddSingleton(s => new DetectionPipeline(
                    s.GetRequiredService<IFileIngester>(),
                    s.GetRequiredService<JunkFilter>(),
                    s.GetRequiredService<FilesRepository>(),
                    SystemTimeProvider.Instance,
                    (msg, ex) => _logger.Information(ex, "{PipelineMessage}", msg)));
                services.AddSingleton(s => new RescanService(
                    s.GetRequiredService<IFileIngester>(),
                    s.GetRequiredService<IIdentityResolver>(),
                    s.GetRequiredService<FilesRepository>(),
                    s.GetRequiredService<JunkFilter>(),
                    s.GetRequiredService<AppMetaRepository>(),
                    SystemTimeProvider.Instance,
                    downloadsPathProvider: null,
                    (msg, ex) => _logger.Information(ex, "{RescanMessage}", msg)));

                services.AddSingleton<IAddContextDialogService, AddContextDialogService>();
                services.AddSingleton<DataManagementService>();
                services.AddSingleton<UpdateService>();
                services.AddSingleton<IToastPresenter, InAppToastPresenter>();
                services.AddSingleton(s => new ToastService(
                    s.GetRequiredService<ILogger>(),
                    s.GetRequiredService<FilesRepository>(),
                    s.GetRequiredService<IAddContextDialogService>(),
                    s.GetRequiredService<IToastPresenter>()));

                services.AddSingleton<SearchService>();
                services.AddSingleton(s => new SearchViewModel(
                    s.GetRequiredService<SearchService>(),
                    s.GetRequiredService<FilesRepository>(),
                    s.GetRequiredService<IAddContextDialogService>(),
                    s.GetRequiredService<ILogger>()));
                services.AddSingleton(s => new InboxViewModel(
                    s.GetRequiredService<FilesRepository>(),
                    s.GetRequiredService<IAddContextDialogService>(),
                    s.GetRequiredService<ILogger>()));
                services.AddSingleton(s => new LibraryViewModel(
                    s.GetRequiredService<FilesRepository>(),
                    s.GetRequiredService<ILogger>()));
                services.AddSingleton(s => new MainViewModel(
                    s.GetRequiredService<SearchViewModel>(),
                    s.GetRequiredService<InboxViewModel>(),
                    s.GetRequiredService<LibraryViewModel>()));

                services.AddSingleton(_activator);
                services.AddSingleton(_hotKeyService);
                services.AddSingleton<TrayService>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _activator.ActivateRequested += (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var tray = Services.GetRequiredService<TrayService>();
                tray.ShowTab(MainTab.Search);
            });
        };

        _hotKeyService.HotKeyPressed += (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var tray = Services.GetRequiredService<TrayService>();
                tray.ShowTab(MainTab.Search);
            });
        };

        _settingsService.SettingsChanged += (_, _) =>
        {
            var newSettings = _settingsService.Current;
            LoggerSetup.SetVerbose(newSettings.LogLevelVerbose);
            _settingsService.ApplyStartWithWindows(newSettings.StartWithWindows);

            var hotKey = _hotKeyService.CurrentDescriptor;
            if (hotKey.Modifiers != newSettings.HotKey.Modifiers || hotKey.Key != newSettings.HotKey.Key)
            {
                var result = _hotKeyService.ReRegister(newSettings.HotKey);
                if (result != HotKeyRegistrationResult.Success)
                {
                    _logger.Warning("Failed to re-register hotkey after settings change: {Result}", result);
                }
            }
        };
    }

    public IServiceProvider Services => _host.Services;

    public void Start()
    {
        _logger.Information("Why Save starting up");
        _host.Start();

        _activator.CreateListener();
        var hotKeyResult = _hotKeyService.Register(_activator.HwndSource!, _settingsService.Current.HotKey);
        if (hotKeyResult != HotKeyRegistrationResult.Success)
        {
            _logger.Warning("Could not register global hotkey on startup: {Result}", hotKeyResult);
        }

        _settingsService.ApplyStartWithWindows(_settingsService.Current.StartWithWindows);

        _updateService = Services.GetRequiredService<UpdateService>();
        _updateService.CheckAndApplyStagedUpdate();
        _updateService.Start();

        var watchService = Services.GetRequiredService<FileWatchService>();
        var pipeline = Services.GetRequiredService<DetectionPipeline>();
        var sink = Services.GetRequiredService<IFileEventSink>();
        var toastService = Services.GetRequiredService<ToastService>();
        pipeline.PendingFileDetected += (_, fileId) => toastService.ShowPendingToast(fileId);

        pipeline.Start();

        if (sink is ChannelFileEventSink channelSink)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var evt in channelSink.Channel.Reader.ReadAllAsync())
                    pipeline.Post(evt);
            });
        }

        var watchedFolders = _settingsService.Current.WatchedFolders.Any()
            ? _settingsService.Current.WatchedFolders
            : new List<string> { RescanService.GetDefaultDownloadsPath() };
        watchService.Start(watchedFolders);

        _ = Task.Run(async () =>
        {
            try
            {
                var rescan = Services.GetRequiredService<RescanService>();
                await rescan.RunStartupDiffAsync(watchedFolders);

                _hourlyRescanTimer = new Timer(
                    _ => _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Services.GetRequiredService<RescanService>()
                                .HourlyRescanAsync(watchedFolders);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Hourly rescan failed");
                        }
                    }),
                    null,
                    TimeSpan.FromHours(1),
                    TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Startup diff failed");
            }
        });

        Services.GetRequiredService<TrayService>().Start();
    }

    public void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        _logger.Information("Why Save shutting down");
        Services.GetRequiredService<DetectionPipeline>().StopAsync().GetAwaiter().GetResult();
        Services.GetRequiredService<FileWatchService>().Dispose();
        Services.GetRequiredService<TrayService>().Stop();
        _updateService?.ApplyStagedUpdateOnRestart();
        _updateService?.Dispose();
        _hourlyRescanTimer?.Dispose();
        _host.StopAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _host.Dispose();
        _connection.Dispose();
        _keyStore.Dispose();
        _activator.Dispose();
        _hotKeyService.Dispose();
        _settingsService.Dispose();
        Log.CloseAndFlush();
    }
}
