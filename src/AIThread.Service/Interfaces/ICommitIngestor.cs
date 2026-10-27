using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

public interface ICommitIngestor
{
    Task<CommitIngestDto?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<CommitIngestDto?> SaveAsync(Guid projectId, SaveCommitIngestRequest request, CancellationToken cancellationToken = default);

    Task<CommitIngestResultDto> RunAsync(Guid projectId, CancellationToken cancellationToken = default);
}
