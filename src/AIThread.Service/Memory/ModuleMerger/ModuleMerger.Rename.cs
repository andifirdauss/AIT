using AIThread.Common.Memory;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    // A memory's main module or one of its extra modules is renamed; when the memory already has the target, the source
    // name is dropped (an undo then gives the row the source back in place of the target).
    private static (string? Module, string? ExtraModules)? Rename(string? module, string? extraCsv, string from, string into)
    {
        if (!MemoryLabels.HasModule(module, extraCsv, from)) return null;
        var all = MemoryLabels.AllModules(module, MemoryLabels.SplitCsv(extraCsv))
            .Select(m => string.Equals(m, from, StringComparison.OrdinalIgnoreCase) ? into : m)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (all[0], MemoryLabels.JoinCsv(all.Skip(1)));
    }
}
