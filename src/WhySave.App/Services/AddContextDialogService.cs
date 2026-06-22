using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WhySave.App.ViewModels;
using WhySave.Storage.Repositories;

namespace WhySave.App.Services;

public sealed class AddContextDialogService : IAddContextDialogService
{
    private readonly IServiceProvider _services;

    public AddContextDialogService(IServiceProvider services)
    {
        _services = services;
    }

    public void ShowAddContext(string fileId)
    {
        if (Application.Current is null)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var filesRepository = _services.GetRequiredService<FilesRepository>();
            var record = filesRepository.GetById(fileId);
            if (record is null)
                return;

            var recentProjects = filesRepository.GetRecentProjects(50).ToList();
            var viewModel = new AddContextViewModel(
                record,
                filesRepository,
                _services.GetRequiredService<ILogger>(),
                recentProjects);

            var window = new AddContextWindow(viewModel);
            window.Closed += (_, _) => RefreshInboxIfVisible();
            window.Show();
            window.Activate();
        });
    }

    private static void RefreshInboxIfVisible()
    {
        var mainWindow = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (mainWindow?.DataContext is MainViewModel vm)
        {
            vm.Inbox.Refresh();
        }
    }
}
