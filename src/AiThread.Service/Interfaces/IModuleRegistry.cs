using AiThread.Common.Entities;

namespace AiThread.Service.Interfaces;

public interface IModuleRegistry
{
    Task<IReadOnlyList<ProjectModule>> ListAsync(string key, bool includeMerged = false, CancellationToken cancellationToken = default);

    Task<string?> ResolveAsync(string key, string? name, CancellationToken cancellationToken = default);

    Task<(string Name, bool Created)> EnsureAsync(string key, string name, string? description = null, IEnumerable<string>? aliases = null, CancellationToken cancellationToken = default);

    Task<ProjectModule?> UpdateAsync(string key, string name, string? description, IReadOnlyList<string>? aliases, CancellationToken cancellationToken);

    IReadOnlyDictionary<string, IReadOnlyList<string>> DrainCreated();

    Task<bool> WaitForCreatedAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
