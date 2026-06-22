using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WhySave.App.Services;
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

    public AppBootstrapper()
    {
        _logger = LoggerSetup.CreateLogger();
        Log.Logger = _logger;

        AppPaths.EnsureAppDataDirExists();

        _connection = SqliteConnectionFactory.Create(AppPaths.DatabasePath);
        new DatabaseMigrator(_connection).MigrateAsync();

        _keyStore = new DpapiKeyStore(AppPaths.KeyPath);
        var key = _keyStore.GetOrCreateKey();

        _host = new HostBuilder()
            .UseSerilog(_logger, dispose: true)
            .ConfigureServices(services =>
            {
                services.AddSingleton(_connection);
                services.AddSingleton(_keyStore);
                services.AddSingleton(s => new FilesRepository(_connection, key));
                services.AddSingleton(s => new AppMetaRepository(_connection));
                services.AddSingleton<IIdentityResolver>(s => new IdentityResolver(s.GetRequiredService<FilesRepository>()));
                services.AddSingleton<IFileIngester>(s => new FileIngester(
                    s.GetRequiredService<IIdentityResolver>(),
                    s.GetRequiredService<FilesRepository>(),
                    s.GetRequiredService<AppMetaRepository>()));
                services.AddSingleton(s => new JunkFilter());
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

                services.AddSingleton<TrayService>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    public IServiceProvider Services => _host.Services;

    public void Start()
    {
        _logger.Information("Why Save starting up");
        _host.Start();

        var watchService = Services.GetRequiredService<FileWatchService>();
        var pipeline = Services.GetRequiredService<DetectionPipeline>();
        var sink = Services.GetRequiredService<IFileEventSink>();

        pipeline.Start();

        if (sink is ChannelFileEventSink channelSink)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var evt in channelSink.Channel.Reader.ReadAllAsync())
                    pipeline.Post(evt);
            });
        }

        var watchedFolders = new[] { RescanService.GetDefaultDownloadsPath() };
        watchService.Start(watchedFolders);

        _ = Task.Run(async () =>
        {
            try
            {
                var rescan = Services.GetRequiredService<RescanService>();
                await rescan.RunStartupDiffAsync(watchedFolders);
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
        _logger.Information("Why Save shutting down");
        Services.GetRequiredService<DetectionPipeline>().StopAsync().GetAwaiter().GetResult();
        Services.GetRequiredService<FileWatchService>().Dispose();
        Services.GetRequiredService<TrayService>().Stop();
        _host.StopAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Stop();
        _host.Dispose();
        _connection.Dispose();
        _keyStore.Dispose();
        Log.CloseAndFlush();
    }
}
