namespace AiThread.UI.Hosting;

// Per-user folders shared by the exe and scripts/aithread.ps1, so both see the same logs and settings.
public static class AppPaths
{
    public const int DefaultPort = 5225;
    public const int KeepLogRuns = 10;

    public static string HomeDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiThread");

    public static string LogDir { get; } = Path.Combine(HomeDir, "logs");

    // Optional overrides layered over the appsettings.json beside the exe; survives reinstalling or `npm update`.
    public static string UserSettingsFile { get; } = Path.Combine(HomeDir, "appsettings.json");

    public static string CliSettingsFile { get; } = Path.Combine(HomeDir, "cli.json");

    public static string ExePath { get; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AiThread.exe");

    public static string NewLogPath() => Path.Combine(LogDir, $"aithread-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    // Newest first; the .err.log files written by the script are not run logs.
    public static FileInfo? LatestLog() => new DirectoryInfo(LogDir).Exists
        ? new DirectoryInfo(LogDir).GetFiles("aithread-*.log")
            .Where(f => !f.Name.EndsWith(".err.log", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Name)
            .FirstOrDefault()
        : null;

    public static void PruneLogs()
    {
        var directory = new DirectoryInfo(LogDir);
        if (!directory.Exists) return;
        foreach (var file in directory.GetFiles("aithread-*.log")
                     .Where(f => !f.Name.EndsWith(".err.log", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(f => f.Name)
                     .Skip(KeepLogRuns - 1))
        {
            TryDelete(file.FullName);
            TryDelete(Path.ChangeExtension(file.FullName, ".err.log"));
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
