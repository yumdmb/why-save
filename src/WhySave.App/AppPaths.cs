using System.IO;

namespace WhySave.App;

public static class AppPaths
{
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WhySave");

    public static string LogsDir { get; } = Path.Combine(AppDataDir, "logs");

    public static string LogFilePath { get; } = Path.Combine(LogsDir, "whysave.log");

    public static string DatabasePath { get; } = Path.Combine(AppDataDir, "whysave.db");

    public static string KeyPath { get; } = Path.Combine(AppDataDir, "key.bin");

    public static string SettingsPath { get; } = Path.Combine(AppDataDir, "settings.json");

    public static void EnsureAppDataDirExists()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(LogsDir);
    }
}
