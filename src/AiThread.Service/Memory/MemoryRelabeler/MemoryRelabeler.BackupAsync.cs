using System.Text.Json;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // F1: everything as it is before the first relabeling, once per label version. No backup, no relabeling.
    private async Task<bool> BackupAsync(CancellationToken cancellationToken)
    {
        if (ExistingBackup() is { } existing)
        {
            _backupPath = existing;
            return true;
        }

        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var file = await transfer.ExportAsync(new MemoryQuery(shortStore.IsConnected ? null : MemoryLevel.Long, null, null, null), cancellationToken);

            List<object> pages;
            await using (var business = await businessStore.CreateAsync(cancellationToken))
                pages = (await business.BusinessPages.AsNoTracking().Select(p => new { p.Id, p.ProjectKey, p.Module, p.Labels }).ToListAsync(cancellationToken))
                    .Cast<object>().ToList();

            var memoryPath = Path.Combine(BackupDirectory, $"memory-before-labels-v{MemoryLabels.CurrentVersion}-{stamp}.json");
            await File.WriteAllBytesAsync(memoryPath + ".tmp", JsonSerializer.SerializeToUtf8Bytes(file, BackupJson), cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(BackupDirectory, $"business-labels-before-v{MemoryLabels.CurrentVersion}-{stamp}.json"),
                JsonSerializer.SerializeToUtf8Bytes(pages, BackupJson), cancellationToken);
            File.Move(memoryPath + ".tmp", memoryPath);

            _backupPath = memoryPath;
            activityLog.Info("memory", $"Backup memory sebelum migrasi label: {file.Items.Count} memory → {memoryPath}");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastMessage = $"Backup memory gagal, migrasi label ditunda: {ex.Message}";
            activityLog.Warn("memory", _lastMessage);
            logger.LogWarning(ex, "Memory backup before relabeling failed.");
            return false;
        }
    }
}
