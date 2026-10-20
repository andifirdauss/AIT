using AiThread.Common.Entities;
using AiThread.Service.CodeCalls;

namespace AiThread.Service.Interfaces;

public interface IFunctionCallStore
{
    Task<IReadOnlyDictionary<string, FunctionScanState>> StatesAsync(Guid projectId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken);

    Task<CodeFunctionScan?> GetAsync(Guid projectId, string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<FunctionCaller>> CallersAsync(Guid projectId, string path, string symbol, int max, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken);

    Task SaveAsync(Guid projectId, IReadOnlyList<CodeFunctionScan> scans, CancellationToken cancellationToken);
}
