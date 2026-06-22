using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Serilog;

namespace WhySave.App.Services;

public sealed class SettingsService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly FileSystemWatcher? _watcher;
    private AppSettings _settings;

    public SettingsService(string path, ILogger logger)
    {
        _path = path;
        _logger = logger;
        _settings = LoadInternal();

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            _watcher = new FileSystemWatcher(directory)
            {
                Filter = Path.GetFileName(_path),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) =>
            {
                try
                {
                    var reloaded = LoadInternal();
                    Interlocked.Exchange(ref _settings, reloaded);
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to reload settings from disk");
                }
            };
        }
    }

    public AppSettings Current => _settings;

    public event EventHandler? SettingsChanged;

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_path, json);
        _settings = settings;
        _logger.Information("Settings saved to {SettingsPath}", _path);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> mutate)
    {
        var copy = DeepCopy(_settings);
        mutate(copy);
        Save(copy);
    }

    public void ApplyStartWithWindows(bool startWithWindows)
    {
        const string runKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string valueName = "WhySave";

        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        if (key is null)
        {
            _logger.Warning("Could not open HKCU Run registry key");
            return;
        }

        if (startWithWindows)
        {
            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WhySave.App.exe");
            key.SetValue(valueName, exePath);
            _logger.Information("Added WhySave to HKCU Run");
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            _logger.Information("Removed WhySave from HKCU Run");
        }
    }

    private AppSettings LoadInternal()
    {
        if (!File.Exists(_path))
        {
            _logger.Information("Settings file not found at {SettingsPath}; using defaults", _path);
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is not null)
                return loaded;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load settings from {SettingsPath}; using defaults", _path);
        }

        return new AppSettings();
    }

    private static AppSettings DeepCopy(AppSettings source) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions) ?? new AppSettings();

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
