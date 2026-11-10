using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// How fast the active nodes are, as an estimate of one small change: no thread, no file change, no memory.
public interface INodeBenchmarkService
{
    Task<NodeBenchmarkDto> RunAsync(CancellationToken cancellationToken = default);
}
