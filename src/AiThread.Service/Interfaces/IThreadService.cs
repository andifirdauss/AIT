using AiThread.Common.DTOs;

namespace AiThread.Service.Interfaces;

public interface IThreadService
{
    Task<IReadOnlyList<ThreadSummaryDto>> GetRecentAsync(int take, Guid? projectId, CancellationToken cancellationToken = default);

    Task<ThreadDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    bool Cancel(Guid id);
}
