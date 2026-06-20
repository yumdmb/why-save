using System.Windows;
using WhySave.App.Infrastructure;

namespace WhySave.App;

public partial class App : Application
{
    private AppBootstrapper? _bootstrapper;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        base.OnExit(e);
    }
}

