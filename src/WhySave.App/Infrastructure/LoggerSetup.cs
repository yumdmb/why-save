using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace WhySave.App.Infrastructure;

public static class LoggerSetup
{
    public const int RetainedFileCountLimit = 5;
    public const long FileSizeLimitBytes = 1024 * 1024;

    public static LoggingLevelSwitch LevelSwitch { get; } = new(LogEventLevel.Information);

    public static ILogger CreateLogger()
    {
        AppPaths.EnsureAppDataDirExists();

        return new Serilog.LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .WriteTo.File(
                AppPaths.LogFilePath,
                rollingInterval: RollingInterval.Infinite,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCountLimit,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static void SetVerbose(bool verbose)
    {
        LevelSwitch.MinimumLevel = verbose ? LogEventLevel.Verbose : LogEventLevel.Information;
    }
}
