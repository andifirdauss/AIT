using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    public static ModuleMergeDto ToDto(ModuleMergeLog log)
    {
        var moves = string.IsNullOrEmpty(log.Moved) ? null : JsonSerializer.Deserialize<ModuleMergeMoves>(log.Moved, Json);
        return new ModuleMergeDto(log.Id, log.FromModule, log.IntoModule, log.Status, log.Reason, log.Confidence, log.DecidedBy, log.CreatedAt,
            log.DecidedAt, (moves?.LongMemories.Count ?? 0) + (moves?.ShortMemories.Count ?? 0), moves?.CodeMap.Count ?? 0, moves?.Pages.Count ?? 0);
    }
}
