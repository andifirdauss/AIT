using AIThread.Common.Entities;
using AIThread.Common.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class ModuleRegistry
{
    public async Task<ProjectModule?> UpdateAsync(string key, string name, string? description, IReadOnlyList<string>? aliases, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        var module = await db.ProjectModules.FirstOrDefaultAsync(m => m.ProjectKey == key && m.Name == name, cancellationToken);
        if (module is null) return null;
        if (description is not null) module.Description = Clip(description.Trim(), 500);
        if (aliases is not null)
        {
            module.Aliases = null;
            MergeAliases(module, aliases.Select(MemoryLabels.Module).OfType<string>()
                .Where(a => a.Length > 1 && !string.Equals(a, module.Name, StringComparison.OrdinalIgnoreCase)));
        }
        module.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return module;
    }
}
