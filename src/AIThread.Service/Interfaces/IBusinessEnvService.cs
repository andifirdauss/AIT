using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

public interface IBusinessEnvService
{
    Task<int> RefreshAsync(string projectKey, CancellationToken cancellationToken = default);

    Task<BusinessEnvOverviewDto?> GetPagesAsync(Guid projectId, string environment, CancellationToken cancellationToken = default);

    Task<BusinessEnvPageDto?> GetPageAsync(Guid pageId, string environment, CancellationToken cancellationToken = default);

    Task<BusinessCompareDto?> CompareAsync(Guid projectId, string left, string right, CancellationToken cancellationToken = default);
}
