using AiThread.Common.DTOs;
using AiThread.Common.Memory;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<ProjectModuleDto?> UpdateModuleAsync(Guid? projectId, bool knowledge, string name, SaveModuleRequest request, CancellationToken cancellationToken = default)
    {
        var key = await KeyAsync(projectId, knowledge, cancellationToken);
        var aliases = request.Aliases;
        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != name)
        {
            var old = name;
            name = await merger.RenameAsync(key, name, request.Name, await DeciderAsync(cancellationToken), cancellationToken);
            // The aliases sent with the rename were read before it: the old name stays an alias (unless only the casing changed).
            if (aliases is not null && MemoryLabels.Compact(old) != MemoryLabels.Compact(name)) aliases = [.. aliases, old];
        }
        var module = await registry.UpdateAsync(key, name, request.Description, aliases, cancellationToken);
        if (module is null) return null;
        return (await GetModulesAsync(projectId, knowledge, cancellationToken)).FirstOrDefault(m => m.Id == module.Id);
    }
}
