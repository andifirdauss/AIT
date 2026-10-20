using AIThread.Common.Memory;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private static string? ExistingBackup()
    {
        if (!Directory.Exists(BackupDirectory)) return null;
        return Directory.GetFiles(BackupDirectory, $"memory-before-labels-v{MemoryLabels.CurrentVersion}-*.json").OrderDescending().FirstOrDefault();
    }
}
