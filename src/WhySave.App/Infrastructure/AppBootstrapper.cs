using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WhySave.App.Services;

namespace WhySave.App.Infrastructure;

public sealed class AppBootstrapper : IDisposable
{
    private readonly IHost _host;
    private readonly ILogger _logger;

    public AppBootstrapper()
    {
        _logger = LoggerSetup.CreateLogger();
        Log.Logger = _logger;

        _host = new HostBuilder()
            .UseSerilog(_logger, dispose: true)
            .ConfigureServices(services =>
            {
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
        Services.GetRequiredService<TrayService>().Start();
    }

    public void Stop()
    {
        _logger.Information("Why Save shutting down");
        Services.GetRequiredService<TrayService>().Stop();
        _host.StopAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _host.Dispose();
        Log.CloseAndFlush();
    }
}
