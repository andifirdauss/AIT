using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // Once, before the first facts of an older version are folded (and deleted): the long project memory as it is, in
    // the import format of the Memory menu. No backup, no migration of facts.
    private async Task<bool> BackupAsync(CancellationToken cancellationToken)
    {
        var directory = MemoryRelabeler.BackupDirectory;
        if (Directory.Exists(directory) && Directory.GetFiles(directory, $"{BackupPrefix}*.json").Length > 0) return true;
        try
        {
            Directory.CreateDirectory(directory);
            var file = await transfer.ExportAsync(new MemoryQuery(MemoryLevel.Long, MemoryType.Project, null, null,
                IncludeUnitTests: false, IncludeTestAccounts: false), cancellationToken);
            var path = Path.Combine(directory, $"{BackupPrefix}{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllBytesAsync(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(file, BackupJson), cancellationToken);
            File.Move(path + ".tmp", path);
            activityLog.Info("memory", $"Backup memory sebelum migrasi ke catatan perubahan: {file.Items.Count} memory → {path}");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activityLog.Warn("memory", $"Backup memory gagal, migrasi memory lama ke ringkasan modul ditunda: {ex.Message}");
            logger.LogWarning(ex, "Memory backup before the change log migration failed.");
            return false;
        }
    }
}
