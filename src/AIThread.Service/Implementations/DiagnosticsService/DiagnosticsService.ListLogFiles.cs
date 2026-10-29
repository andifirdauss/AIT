using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class DiagnosticsService
{
    // Newest first. The file this process writes is the most recent aithread-*.log while the exe runs in menu/tray mode.
    public IReadOnlyList<LogFileDto> ListLogFiles()
    {
        var directory = new DirectoryInfo(LogRoot);
        if (!directory.Exists) return [];
        var files = directory.GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        var current = files.FirstOrDefault(f => f.Name.StartsWith("aithread-", StringComparison.OrdinalIgnoreCase)
                                                && !f.Name.EndsWith(".err.log", StringComparison.OrdinalIgnoreCase))?.Name;
        return files.Select(f => new LogFileDto(f.Name, f.Length, f.LastWriteTimeUtc, f.Name == current)).ToList();
    }
}
