using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
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
    private readonly JunkFilter _junkFilter;
    private readonly DataManagementService _dataManagement;
    private readonly ILogger _logger;

    [ObservableProperty]
    private ObservableCollection<string> _watchedFolders = new();

    [ObservableProperty]
    private string _blockGlobsText = "";

    [ObservableProperty]
    private string _allowGlobsText = "";

    [ObservableProperty]
    private int _minSizeKB = 1;

    [ObservableProperty]
    private string _hotKeyText = "";

    [ObservableProperty]
    private string _hotKeyStatus = "";

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string _encryptionStatus = "";

    [ObservableProperty]
    private string _rotateKeyStatus = "";

    [ObservableProperty]
    private bool _autoUpdateEnabled;

    [ObservableProperty]
    private bool _logLevelVerbose;

    [ObservableProperty]
    private string _junkRulesStatus = "";

    public SettingsViewModel(
        SettingsService settingsService,
        FileWatchService fileWatchService,
        GlobalHotKeyService hotKeyService,
        JunkFilter junkFilter,
        DataManagementService dataManagement,
        ILogger logger)
    {
        _settingsService = settingsService;
        _fileWatchService = fileWatchService;
        _hotKeyService = hotKeyService;
        _junkFilter = junkFilter;
        _dataManagement = dataManagement;
        _logger = logger;

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.Current;

        WatchedFolders = new ObservableCollection<string>(s.WatchedFolders.Any()
            ? s.WatchedFolders
            : new List<string> { RescanService.GetDefaultDownloadsPath() });

        BlockGlobsText = string.Join(Environment.NewLine, s.JunkRules.BlockGlobs);
        AllowGlobsText = string.Join(Environment.NewLine, s.JunkRules.AllowGlobs);
        MinSizeKB = (int)(s.JunkRules.MinSizeBytes / 1024);

        HotKeyText = s.HotKey.ToString();
        StartWithWindows = s.StartWithWindows;
        AutoUpdateEnabled = s.AutoUpdateChannel == "stable";
        LogLevelVerbose = s.LogLevelVerbose;

        EncryptionStatus = _dataManagement.GetEncryptionStatus();
    }

    [RelayCommand]
    private void AddWatchedFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder to watch",
        };

        if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.FolderName))
            return;

        var folder = dialog.FolderName;
        if (WatchedFolders.Contains(folder))
        {
            _logger.Information("Folder already watched: {Folder}", folder);
            return;
        }

        WatchedFolders.Add(folder);
        _fileWatchService.AddFolder(folder);
        SaveWatchedFolders();
        _logger.Information("Added watched folder: {Folder}", folder);
    }

    [RelayCommand]
    private void RemoveWatchedFolder(string folder)
    {
        if (!WatchedFolders.Remove(folder))
            return;

        _fileWatchService.RemoveFolder(folder);
        SaveWatchedFolders();
        _logger.Information("Removed watched folder: {Folder}", folder);
    }

    private void SaveWatchedFolders()
    {
        _settingsService.Update(s =>
        {
            s.WatchedFolders = WatchedFolders.ToList();
        });
    }

    [RelayCommand]
    private void SaveJunkRules()
    {
        var blockGlobs = ParseLines(BlockGlobsText);
        var allowGlobs = ParseLines(AllowGlobsText);
        var minSizeBytes = (long)MinSizeKB * 1024;

        _junkFilter.UpdateRules(new JunkFilterRules
        {
            BlockGlobs = blockGlobs,
            AllowGlobs = allowGlobs,
            MinSizeBytes = minSizeBytes,
        });

        _settingsService.Update(s =>
        {
            s.JunkRules = new JunkRulesSettings
            {
                BlockGlobs = blockGlobs,
                AllowGlobs = allowGlobs,
                MinSizeBytes = minSizeBytes,
            };
        });

        JunkRulesStatus = "Junk rules applied.";
        _logger.Information("Junk rules updated and applied");
    }

    [RelayCommand]
    private void ResetJunkRules()
    {
        BlockGlobsText = string.Join(Environment.NewLine, JunkFilter.DefaultBlockGlobs);
        AllowGlobsText = "";
        MinSizeKB = 1;
        JunkRulesStatus = "Reverted to defaults. Click Apply to save.";
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
    private void RotateKey()
    {
        if (_dataManagement.RotateKey(out var message))
            RotateKeyStatus = message;
        else
            RotateKeyStatus = message;

        EncryptionStatus = _dataManagement.GetEncryptionStatus();
    }

    [RelayCommand]
    private void ExportData()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Why Save data",
            Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"whysave-export-{DateTime.Now:yyyy-MM-dd}.json",
        };

        if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.FileName))
            return;

        if (_dataManagement.ExportData(dialog.FileName, out var message))
            RotateKeyStatus = message;
        else
            RotateKeyStatus = message;
    }

    [RelayCommand]
    private void ClearData()
    {
        var result = System.Windows.MessageBox.Show(
            "This will permanently delete ALL file records, including saved reasons and notes. This cannot be undone. Are you sure?",
            "Clear all data",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        _dataManagement.ClearAllData(out var clearMessage);
        RotateKeyStatus = clearMessage;
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Update(s =>
        {
            s.StartWithWindows = StartWithWindows;
            s.AutoUpdateChannel = AutoUpdateEnabled ? "stable" : "off";
            s.LogLevelVerbose = LogLevelVerbose;
        });

        _settingsService.ApplyStartWithWindows(StartWithWindows);
        LoggerSetup.SetVerbose(LogLevelVerbose);

        _logger.Information("Settings saved");
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private static List<string> ParseLines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();

    public event EventHandler? RequestClose;
}
