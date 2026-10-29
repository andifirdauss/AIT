using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

public interface IThreadService
{
    Task<IReadOnlyList<ThreadSummaryDto>> GetRecentAsync(int take, Guid? projectId, string? search = null, CancellationToken cancellationToken = default);

    Task<ThreadDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    bool Cancel(Guid id);
}
