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

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
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
                window.Closed += async (_, _) => await RefreshMainWindowAfterSaveAsync(viewModel.Saved);
                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
                var logger = _services.GetRequiredService<ILogger>();
                logger.Error(ex, "Failed to open Add Context window for {FileId}", fileId);
                MessageBox.Show(
                    $"Failed to open the Add Context form:\n\n{ex.Message}",
                    "Why Save - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }));
    }

    private static async Task RefreshMainWindowAfterSaveAsync(bool saved)
    {
        if (!saved)
            return;

        var mainWindow = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (mainWindow?.DataContext is MainViewModel vm)
        {
            vm.Inbox.Refresh();
            await vm.Find.RefreshAsync();
        }
    }
}
