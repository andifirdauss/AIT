using AIThread.Common.Entities;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    // The module to keep: the more used one, then the shorter (singular) name.
    private static (ProjectModule From, ProjectModule Into) Keep(ProjectModule a, ProjectModule b, IReadOnlyDictionary<string, int> usage)
    {
        var ua = usage.GetValueOrDefault(a.Name);
        var ub = usage.GetValueOrDefault(b.Name);
        if (ua != ub) return ua > ub ? (b, a) : (a, b);
        return a.Name.Length <= b.Name.Length ? (b, a) : (a, b);
    }
}
