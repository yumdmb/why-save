using System.Windows;
using Serilog;
using WhySave.App.Infrastructure;
using WhySave.App.Services;
using WhySave.Native;

namespace WhySave.App;

public partial class App : Application
{
    private AppBootstrapper? _bootstrapper;
    private SingleInstanceMutex? _singleInstanceMutex;
    private ILogger _logger = LoggerSetup.CreateLogger();

    public AppBootstrapper? Bootstrapper => _bootstrapper;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new SingleInstanceMutex();
        if (!_singleInstanceMutex.IsFirstInstance)
        {
            _logger.Information("Second instance detected; activating first instance and exiting");
            SingleInstanceActivator.TryActivateFirstInstance(_logger);
            Shutdown();
            return;
        }

        _bootstrapper = new AppBootstrapper();
        _bootstrapper.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_bootstrapper is not null)
        {
            _bootstrapper.Stop();
            _bootstrapper.Dispose();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
