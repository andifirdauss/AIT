using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

public interface IBusinessDigestRunner
{
    bool IsRunning(string projectKey);

    Task<BusinessDigestResultDto> RunProjectAsync(Guid projectId, CancellationToken cancellationToken);

    Task RunAllAsync(CancellationToken cancellationToken);
}
