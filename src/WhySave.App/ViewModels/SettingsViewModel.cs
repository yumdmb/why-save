using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WhySave.App.Infrastructure;
using WhySave.App.Services;
using WhySave.Core;

namespace WhySave.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly FileWatchService _fileWatchService;
    private readonly GlobalHotKeyService _hotKeyService;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _hotKeyText = "";

    [ObservableProperty]
    private string _hotKeyStatus = "";

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _logLevelVerbose;

    public SettingsViewModel(
        SettingsService settingsService,
        FileWatchService fileWatchService,
        GlobalHotKeyService hotKeyService,
        ILogger logger)
    {
        _settingsService = settingsService;
        _fileWatchService = fileWatchService;
        _hotKeyService = hotKeyService;
        _logger = logger;

        HotKeyText = _settingsService.Current.HotKey.ToString();
        StartWithWindows = _settingsService.Current.StartWithWindows;
        LogLevelVerbose = _settingsService.Current.LogLevelVerbose;
    }

    [RelayCommand]
    private void ApplyHotKey()
    {
        if (!HotKeyParser.TryParse(HotKeyText, out var descriptor))
        {
            HotKeyStatus = "Invalid hotkey format. Use e.g. Ctrl+Win+Y";
            _logger.Warning("User entered invalid hotkey: {HotKeyText}", HotKeyText);
            return;
        }

        var result = _hotKeyService.ReRegister(descriptor);
        switch (result)
        {
            case HotKeyRegistrationResult.Success:
                _settingsService.Update(s => s.HotKey = descriptor.Clone());
                HotKeyStatus = $"Hotkey set to {descriptor}";
                _logger.Information("Hotkey changed to {Descriptor}", descriptor);
                break;

            case HotKeyRegistrationResult.AlreadyInUse:
                HotKeyStatus = $"{descriptor} is already in use by another application. Choose a different combination.";
                _logger.Warning("Hotkey conflict: {Descriptor}", descriptor);
                break;

            case HotKeyRegistrationResult.Invalid:
                HotKeyStatus = "Invalid hotkey.";
                break;

            default:
                HotKeyStatus = "Failed to register hotkey.";
                _logger.Warning("Hotkey registration failed with result {Result}", result);
                break;
        }
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Update(s =>
        {
            s.StartWithWindows = StartWithWindows;
            s.LogLevelVerbose = LogLevelVerbose;
        });

        _settingsService.ApplyStartWithWindows(StartWithWindows);
        LoggerSetup.SetVerbose(LogLevelVerbose);

        _logger.Information("Settings saved");
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? RequestClose;
}
