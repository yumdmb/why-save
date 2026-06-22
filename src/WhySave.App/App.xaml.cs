using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.Notifications;
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
    private ToastNotificationActivatedEventArgsCompat? _pendingToastArgs;

    public AppBootstrapper? Bootstrapper => _bootstrapper;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

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

        if (_pendingToastArgs is not null)
        {
            try
            {
                _bootstrapper.Services.GetRequiredService<ToastService>().HandleActivation(_pendingToastArgs);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to handle pending toast activation");
            }
            _pendingToastArgs = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;

        if (_bootstrapper is not null)
        {
            _bootstrapper.Stop();
            _bootstrapper.Dispose();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        if (_bootstrapper is null)
        {
            _pendingToastArgs = e;
            return;
        }

        try
        {
            _bootstrapper.Services.GetRequiredService<ToastService>().HandleActivation(e);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle toast activation");
        }
    }
}
