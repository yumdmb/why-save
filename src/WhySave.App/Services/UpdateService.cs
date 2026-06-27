using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace WhySave.App.Services;

public sealed class ReleaseFeed
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}

public sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public ReleaseFeed? Feed { get; init; }
    public string? Message { get; init; }
}

public sealed class UpdateService : IDisposable
{
    public const string FeedUrl = "https://github.com/yumdmb/why-save/releases/latest/download/feed.json";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly SettingsService _settingsService;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private Timer? _dailyTimer;
    private readonly string _exePath;
    private readonly string _updatesDir;
    private readonly string _stagedInstallerPath;

    public UpdateService(SettingsService settingsService, ILogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WhySave/1.0 (self-update)");

        _exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WhySave.App.exe");
        _updatesDir = Path.Combine(AppPaths.AppDataDir, "updates");
        _stagedInstallerPath = Path.Combine(_updatesDir, "WhySave-update.msi");
    }

    public void Start()
    {
        if (!IsAutoUpdateEnabled)
        {
            _logger.Information("Auto-update is disabled; skipping feed check");
            return;
        }

        _ = Task.Run(async () => await CheckAndStageAsync());

        _dailyTimer = new Timer(
            _ => _ = Task.Run(async () => await CheckAndStageAsync()),
            null,
            CheckInterval,
            CheckInterval);

        _logger.Information("Update service started; checking feed on launch and every {Hours} hours", CheckInterval.TotalHours);
    }

    private async Task CheckAndStageAsync()
    {
        var result = await CheckForUpdateAsync();
        if (result.UpdateAvailable && result.Feed is not null)
        {
            await DownloadAndStageUpdateAsync(result.Feed);
        }
    }

    private bool IsAutoUpdateEnabled =>
        string.Equals(_settingsService.Current.AutoUpdateChannel, "stable", StringComparison.OrdinalIgnoreCase);

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!IsAutoUpdateEnabled)
            return new UpdateCheckResult { Message = "Auto-update is disabled" };

        try
        {
            _logger.Information("Checking release feed for updates");

            using var response = await _httpClient.GetAsync(FeedUrl, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var feed = JsonSerializer.Deserialize<ReleaseFeed>(json);

            if (feed is null || string.IsNullOrEmpty(feed.Version))
            {
                _logger.Warning("Release feed returned empty or invalid payload");
                return new UpdateCheckResult { Message = "Invalid feed response" };
            }

            var currentVersion = GetCurrentVersion();
            var feedVersion = new Version(feed.Version);

            if (feedVersion <= currentVersion)
            {
                _logger.Information("Already up to date (current={Current}, feed={Feed})", currentVersion, feedVersion);
                return new UpdateCheckResult { UpdateAvailable = false, Feed = feed, Message = "Already up to date" };
            }

            _logger.Information("Update available: {FeedVersion} (current {CurrentVersion})", feedVersion, currentVersion);
            return new UpdateCheckResult { UpdateAvailable = true, Feed = feed, Message = $"Update {feedVersion} available" };
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to check release feed");
            return new UpdateCheckResult { Message = "Feed check failed" };
        }
    }

    public async Task<bool> DownloadAndStageUpdateAsync(ReleaseFeed feed, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_updatesDir);
            var downloadedPath = Path.Combine(Path.GetTempPath(), $"WhySave-update-{feed.Version}.msi");

            _logger.Information("Downloading MSI update from feed URL");

            using (var response = await _httpClient.GetAsync(feed.Url, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = File.Create(downloadedPath);
                await response.Content.CopyToAsync(fs, ct);
            }

            var actualHash = await ComputeSha256Async(downloadedPath, ct);
            if (!string.Equals(actualHash, feed.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning("Update hash mismatch: expected={Expected}, actual={Actual}", feed.Sha256, actualHash);
                try { File.Delete(downloadedPath); } catch { }
                return false;
            }

            File.Copy(downloadedPath, _stagedInstallerPath, overwrite: true);
            try { File.Delete(downloadedPath); } catch { }

            _logger.Information("MSI update staged at {InstallerPath}; will install after app exit", _stagedInstallerPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to download and stage update");
            return false;
        }
    }

    public void ApplyStagedUpdateOnRestart()
    {
        if (!File.Exists(_stagedInstallerPath))
            return;

        var batchPath = Path.Combine(Path.GetTempPath(), "whysave-updater.bat");

        var batch = $"""
            @echo off
            :wait
            timeout /t 1 /nobreak >nul
            tasklist /fi "PID eq {Environment.ProcessId}" 2>nul | find "{Environment.ProcessId}" >nul
            if not errorlevel 1 goto wait
            start /wait "" msiexec.exe /i "{_stagedInstallerPath}" /qn /norestart
            set MSI_EXIT=%ERRORLEVEL%
            if "%MSI_EXIT%"=="0" del "{_stagedInstallerPath}" >nul 2>&1
            if "%MSI_EXIT%"=="3010" del "{_stagedInstallerPath}" >nul 2>&1
            if "%MSI_EXIT%"=="0" start "" "{_exePath}"
            if "%MSI_EXIT%"=="3010" start "" "{_exePath}"
            del "%~f0" >nul 2>&1
            """;

        File.WriteAllText(batchPath, batch);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batchPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process.Start(psi);
        _logger.Information("Updater stub launched; installing staged MSI after app exit");
    }

    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(1, 0, 0);
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        _dailyTimer?.Dispose();
        _httpClient.Dispose();
    }
}
