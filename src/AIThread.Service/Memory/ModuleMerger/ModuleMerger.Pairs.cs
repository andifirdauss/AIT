using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    private static IEnumerable<(ProjectModule A, ProjectModule B)> Pairs(IReadOnlyList<ProjectModule> modules, IReadOnlyCollection<string>? focus)
    {
        for (var i = 0; i < modules.Count; i++)
        for (var j = i + 1; j < modules.Count; j++)
        {
            if (focus is not null && !focus.Contains(modules[i].Name, StringComparer.OrdinalIgnoreCase)
                                  && !focus.Contains(modules[j].Name, StringComparer.OrdinalIgnoreCase)) continue;
            yield return (modules[i], modules[j]);
        }
    }
}
