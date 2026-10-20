using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

public interface IThreadRollback
{
    Task<RollbackPreviewDto?> PreviewAsync(Guid threadId, CancellationToken cancellationToken = default);

    Task<RollbackResultDto> RollbackAsync(Guid threadId, CancellationToken cancellationToken = default);
}
