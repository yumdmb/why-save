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
    public const string FeedUrl = "https://releases.whysave.app/feed.json";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly SettingsService _settingsService;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private Timer? _dailyTimer;
    private readonly string _exePath;
    private readonly string _installDir;

    public UpdateService(SettingsService settingsService, ILogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WhySave/1.0 (self-update)");

        _exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WhySave.App.exe");
        _installDir = Path.GetDirectoryName(_exePath) ?? AppContext.BaseDirectory;
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
            var tempPath = Path.Combine(_installDir, "WhySave.App.exe.new");
            var downloadedPath = Path.Combine(Path.GetTempPath(), $"WhySave-update-{feed.Version}.exe");

            _logger.Information("Downloading update from feed URL");

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

            File.Copy(downloadedPath, tempPath, overwrite: true);
            try { File.Delete(downloadedPath); } catch { }

            _logger.Information("Update staged at {TempPath}; will apply on next restart", tempPath);
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
        var stagedPath = Path.Combine(_installDir, "WhySave.App.exe.new");
        if (!File.Exists(stagedPath))
            return;

        var batchPath = Path.Combine(Path.GetTempPath(), "whysave-updater.bat");
        var oldPath = _exePath + ".old";

        var batch = $"""
            @echo off
            :wait
            timeout /t 1 /nobreak >nul
            tasklist /fi "PID eq {Environment.ProcessId}" 2>nul | find "{Environment.ProcessId}" >nul
            if not errorlevel 1 goto wait
            move /y "{_exePath}" "{oldPath}" >nul 2>&1
            move /y "{stagedPath}" "{_exePath}" >nul 2>&1
            start "" "{_exePath}"
            del "{oldPath}" >nul 2>&1
            del "%~f0" >nul 2>&1
            """;

        File.WriteAllText(batchPath, batch);

        var psi = new ProcessStartInfo
        {
            FileName = batchPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process.Start(psi);
        _logger.Information("Updater stub launched; applying staged update on exit");
    }

    public void CheckAndApplyStagedUpdate()
    {
        var stagedPath = Path.Combine(_installDir, "WhySave.App.exe.new");
        if (File.Exists(stagedPath))
        {
            try
            {
                var oldPath = _exePath + ".old";
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }

                File.Move(_exePath, oldPath);
                File.Move(stagedPath, _exePath);

                _logger.Information("Applied staged update from previous session");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to apply staged update on startup");
            }
        }

        var leftoverOld = _exePath + ".old";
        if (File.Exists(leftoverOld))
        {
            try { File.Delete(leftoverOld); } catch { }
        }
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
