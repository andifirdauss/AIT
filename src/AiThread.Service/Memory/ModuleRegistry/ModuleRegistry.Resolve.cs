using AiThread.Common.Entities;
using AiThread.Common.Memory;

namespace AiThread.Service.Memory;

public sealed partial class ModuleRegistry
{
    // The active module a name stands for, or null when the key has no such module. `modules` must include the merged
    // ones (redirects) for old names to resolve.
    public static string? Resolve(IReadOnlyList<ProjectModule> modules, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var compact = MemoryLabels.Compact(name);
        if (compact.Length == 0) return null;

        var match = modules.FirstOrDefault(m => MemoryLabels.Compact(m.Name) == compact)
                    ?? modules.FirstOrDefault(m => MemoryLabels.SplitCsv(m.Aliases).Any(a => MemoryLabels.Compact(a) == compact))
                    ?? modules.FirstOrDefault(m => MemoryLabels.SameName(m.Name, name))
                    ?? modules.FirstOrDefault(m => MemoryLabels.SplitCsv(m.Aliases).Any(a => MemoryLabels.SameName(a, name)));

        // Follow merges; a broken or looping chain ends at the last module found.
        for (var hops = 0; match?.MergedInto is { } next && hops < 8; hops++)
            match = modules.FirstOrDefault(m => string.Equals(m.Name, next, StringComparison.OrdinalIgnoreCase)) ?? match;
        return match is null ? null : match.MergedInto ?? match.Name;
    }
}
