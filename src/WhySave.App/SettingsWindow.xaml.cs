using System.Windows;
using Serilog;
using WhySave.App.Services;
using WhySave.App.ViewModels;
using WhySave.Core;

namespace WhySave.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(
        SettingsService settingsService,
        FileWatchService fileWatchService,
        GlobalHotKeyService hotKeyService,
        JunkFilter junkFilter,
        DataManagementService dataManagement,
        ILogger logger)
    {
        InitializeComponent();

        var vm = new SettingsViewModel(
            settingsService,
            fileWatchService,
            hotKeyService,
            junkFilter,
            dataManagement,
            logger);
        vm.RequestClose += (_, _) => Close();
        DataContext = vm;
    }
}
