using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.Interfaces;

// Planner / executor / wrapper worker configuration.
public interface INodeService
{
    Task<IReadOnlyList<NodeDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<NodeDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<NodeDto> CreateAsync(SaveNodeRequest request, CancellationToken cancellationToken = default);

    Task<NodeDto?> UpdateAsync(Guid id, SaveNodeRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Active nodes of a role ordered by priority.
    Task<IReadOnlyList<AiNode>> GetActiveAsync(NodeRole role, CancellationToken cancellationToken = default);

    // A node with its skills whether active or not: the fallback a failing node hands its call to.
    Task<AiNode?> GetNodeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PipelineStatusDto> GetPipelineStatusAsync(CancellationToken cancellationToken = default);

    Task<NodeTestResultDto> TestAsync(Guid id, CancellationToken cancellationToken = default);
}
