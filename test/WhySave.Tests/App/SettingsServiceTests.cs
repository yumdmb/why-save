using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Serilog;
using WhySave.App.Services;

namespace WhySave.Tests.App;

public class SettingsServiceTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly ILogger _logger;

    public SettingsServiceTests()
    {
        // Use a subdirectory that does not exist yet so the file watcher is not created,
        // keeping tests deterministic and avoiding races with Save.
        _settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"WhySaveSettingsTests_{Guid.NewGuid():N}",
            "settings.json");
        _logger = new LoggerConfiguration().CreateLogger();
    }

    public void Dispose()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (directory is not null && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void Load_Creates_Default_Settings_When_File_Missing()
    {
        var service = new SettingsService(_settingsPath, _logger);

        Assert.NotNull(service.Current);
        Assert.True(service.Current.StartWithWindows);
        Assert.Equal("off", service.Current.AutoUpdateChannel);
        Assert.Equal(HotKeyModifier.Control | HotKeyModifier.Win, service.Current.HotKey.Modifiers);
        Assert.Equal(Key.Y, service.Current.HotKey.Key);
    }

    [Fact]
    public void Save_Persists_Settings_To_Disk()
    {
        var service = new SettingsService(_settingsPath, _logger);
        var settings = new AppSettings
        {
            StartWithWindows = false,
            AutoUpdateChannel = "stable",
            LogLevelVerbose = true,
            HotKey = new HotKeyDescriptor
            {
                Modifiers = HotKeyModifier.Alt | HotKeyModifier.Shift,
                Key = Key.P,
            },
            WatchedFolders = new List<string> { @"C:\Test" },
        };

        service.Save(settings);

        // Verify the in-memory settings were updated.
        Assert.False(service.Current.StartWithWindows);
        Assert.Equal("stable", service.Current.AutoUpdateChannel);
        Assert.True(service.Current.LogLevelVerbose);
        Assert.Equal(Key.P, service.Current.HotKey.Key);
        Assert.Single(service.Current.WatchedFolders);

        // Verify the file on disk can be reloaded with camel-case naming.
        var json = File.ReadAllText(_settingsPath);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        Assert.NotNull(loaded);
        Assert.False(loaded.StartWithWindows);
    }

    [Fact]
    public void Update_Mutates_And_Saves_Current_Settings()
    {
        var service = new SettingsService(_settingsPath, _logger);
        service.Update(s => s.LogLevelVerbose = true);

        Assert.True(service.Current.LogLevelVerbose);
        Assert.True(File.Exists(_settingsPath));
    }

    [Fact]
    public void SettingsChanged_Raised_After_Update()
    {
        var service = new SettingsService(_settingsPath, _logger);
        var raised = false;
        service.SettingsChanged += (_, _) => raised = true;

        service.Update(s => s.StartWithWindows = false);

        Assert.True(raised);
    }
}
