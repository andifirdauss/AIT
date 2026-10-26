namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    private static string? ExistingBackup()
    {
        var directory = MemoryRelabeler.BackupDirectory;
        return Directory.Exists(directory) ? Directory.GetFiles(directory, $"{BackupPrefix}*.json").OrderDescending().FirstOrDefault() : null;
    }
}
