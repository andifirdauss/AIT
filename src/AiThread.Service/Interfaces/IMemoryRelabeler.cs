using AiThread.Common.DTOs;

namespace AiThread.Service.Interfaces;

public interface IMemoryRelabeler
{
    Task<MemoryRelabelStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    Task RunAsync(bool modulesOnly, CancellationToken cancellationToken);
}
