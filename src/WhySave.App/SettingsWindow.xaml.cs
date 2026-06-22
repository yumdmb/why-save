using System.Windows;
using WhySave.App.Services;
using WhySave.App.ViewModels;
using WhySave.Core;
using Serilog;

namespace WhySave.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(
        SettingsService settingsService,
        FileWatchService fileWatchService,
        GlobalHotKeyService hotKeyService,
        ILogger logger)
    {
        InitializeComponent();

        var vm = new SettingsViewModel(settingsService, fileWatchService, hotKeyService, logger);
        vm.RequestClose += (_, _) => Close();
        DataContext = vm;
    }
}
