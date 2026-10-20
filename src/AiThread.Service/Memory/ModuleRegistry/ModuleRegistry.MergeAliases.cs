using AiThread.Common.Entities;
using AiThread.Common.Memory;

namespace AiThread.Service.Memory;

public sealed partial class ModuleRegistry
{
    // True when an alias was added.
    public static bool MergeAliases(ProjectModule module, IEnumerable<string> aliases)
    {
        var current = MemoryLabels.SplitCsv(module.Aliases).ToList();
        var changed = false;
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || current.Any(a => string.Equals(a, alias, StringComparison.OrdinalIgnoreCase))) continue;
            if (string.Join(",", current.Append(alias)).Length > 1000) break;
            current.Add(alias);
            changed = true;
        }
        if (changed) module.Aliases = MemoryLabels.JoinCsv(current);
        return changed;
    }
}
