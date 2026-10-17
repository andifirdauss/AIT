using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Interfaces;

// Short memory lives in Redis for one day, then is consolidated into long memory in the database.
public interface IMemoryService
{
    Task<MemoryItemDto> AddShortAsync(SaveMemoryRequest request, Guid? sourceThreadId = null, CancellationToken cancellationToken = default);

    // Labels only (project + knowledge) — what the planner scans first: the two-layer search over free text.
    Task<IReadOnlyList<MemoryIndexEntry>> GetIndexAsync(Guid? projectId, string? query, int take, CancellationToken cancellationToken = default);

    // Layer 1 narrows by module (given, or detected in the text and in the files it names), optionally layer and kind;
    // layer 2 ranks by keywords. Unlabeled memory (older versions) and the business wiki get their own share.
    Task<IReadOnlyList<MemoryIndexEntry>> SearchAsync(MemorySearch search, CancellationToken cancellationToken = default);

    // The planner's memory map: labeled memories per module, layer and kind.
    Task<IReadOnlyList<MemoryModuleSummary>> GetModuleSummaryAsync(Guid? projectId, CancellationToken cancellationToken = default);

    // Full descriptions for the ids the planner decided to read.
    Task<IReadOnlyList<MemoryItemDto>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<PagedResult<MemoryItemDto>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken = default);

    Task<MemoryItemDto?> GetAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default);

    Task<MemoryItemDto?> UpdateAsync(MemoryLevel level, Guid id, SaveMemoryRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default);

    Task<bool> PromoteAsync(Guid id, CancellationToken cancellationToken = default);

    // Moves short memories older than the short-term window into the database. Returns the number moved.
    Task<int> ConsolidateAsync(CancellationToken cancellationToken = default);

    Task<MemoryStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
}
