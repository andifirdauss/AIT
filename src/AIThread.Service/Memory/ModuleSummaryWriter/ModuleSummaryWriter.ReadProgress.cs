using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    private MigrationProgress? ReadProgress()
    {
        try
        {
            return File.Exists(ProgressFile) ? JsonSerializer.Deserialize<MigrationProgress>(File.ReadAllBytes(ProgressFile), BackupJson) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read the change log migration progress.");
            return null;
        }
    }
}
