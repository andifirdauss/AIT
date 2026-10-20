using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

public interface IMemoryRelabeler
{
    Task<MemoryRelabelStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    Task RunAsync(bool modulesOnly, CancellationToken cancellationToken);
}
