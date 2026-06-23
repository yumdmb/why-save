using System.Windows;
using System.Windows.Threading;
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

        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        Dispatcher.CurrentDispatcher.UnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_bootstrapper is not null)
        {
            _bootstrapper.Dispose();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            _logger.Fatal(ex, "AppDomain unhandled exception (isTerminating={IsTerminating})", e.IsTerminating);
        else
            _logger.Fatal("AppDomain unhandled exception: {ExceptionObject}", e.ExceptionObject);
        Log.CloseAndFlush();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.Error(e.Exception, "UI thread unhandled exception");
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nSee the log file at %LOCALAPPDATA%\\WhySave\\logs\\ for details.",
            "Why Save - Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
