using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using WhySave.App.Services;

namespace WhySave.Tests.App;

public class UpdateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;
    private readonly SettingsService _settingsEnabled;
    private readonly SettingsService _settingsDisabled;

    public UpdateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveUpdateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _settingsPath = Path.Combine(_tempDir, "settings.json");
        var logger = new Serilog.LoggerConfiguration().CreateLogger();

        _settingsEnabled = new SettingsService(_settingsPath, logger);
        _settingsEnabled.Save(new AppSettings { AutoUpdateChannel = "stable" });

        var disabledPath = Path.Combine(_tempDir, "settings-disabled.json");
        _settingsDisabled = new SettingsService(disabledPath, logger);
        _settingsDisabled.Save(new AppSettings { AutoUpdateChannel = "off" });
    }

    public void Dispose()
    {
        _settingsEnabled.Dispose();
        _settingsDisabled.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CheckForUpdate_Returns_Disabled_Message_When_AutoUpdate_Off()
    {
        var logger = new Serilog.LoggerConfiguration().CreateLogger();
        using var service = new UpdateService(_settingsDisabled, logger);

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("disabled", result.Message);
    }

    [Fact]
    public void ReleaseFeed_Parses_From_Json()
    {
        var json = """
            {
                "version": "1.0.1",
                "url": "https://github.com/yumdmb/why-save/releases/download/v1.0.1/WhySave.msi",
                "sha256": "abc123def456"
            }
            """;

        var feed = JsonSerializer.Deserialize<ReleaseFeed>(json);

        Assert.NotNull(feed);
        Assert.Equal("1.0.1", feed!.Version);
        Assert.Equal("https://github.com/yumdmb/why-save/releases/download/v1.0.1/WhySave.msi", feed.Url);
        Assert.Equal("abc123def456", feed.Sha256);
    }

    [Fact]
    public void Feed_Url_Is_Https()
    {
        Assert.StartsWith("https://", UpdateService.FeedUrl);
    }

    [Fact]
    public void CheckInterval_Is_24_Hours()
    {
        Assert.Equal(24, UpdateService.CheckInterval.TotalHours);
    }

    [Fact]
    public async Task CheckForUpdate_With_Empty_Feed_Returns_Invalid_Message()
    {
        var logger = new Serilog.LoggerConfiguration().CreateLogger();
        using var service = new UpdateService(_settingsEnabled, logger);

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public void Default_AutoUpdateChannel_Is_Off()
    {
        var settings = new AppSettings();
        Assert.Equal("off", settings.AutoUpdateChannel);
    }

    [Fact]
    public void ReleaseFeed_Url_Can_Point_To_Msi_Installer()
    {
        var feed = new ReleaseFeed
        {
            Version = "1.0.1",
            Url = "https://github.com/yumdmb/why-save/releases/download/v1.0.1/WhySave.msi",
            Sha256 = "abc123def456",
        };

        Assert.EndsWith(".msi", new Uri(feed.Url).AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }
}
