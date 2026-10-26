using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    // A developer's (or the planner's) decision; never overwritten by rules or the model.
    public async Task<CodeMapHit> SetManualAsync(ProjectContext project, string path, string module, MemoryLayer? layer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException(Lang.T("Project belum punya key; buka lagi menu Proyek sebentar lagi.", "The project has no key yet; open the Project menu again in a moment."));
        var relative = Normalize(project, [path]).FirstOrDefault()
                       ?? throw new AppValidationException(Lang.T($"File '{path}' tidak ditemukan di project.", $"File '{path}' not found in the project."));
        var (name, _) = await registry.EnsureAsync(project.Key, module, cancellationToken: cancellationToken);
        var stored = (await StoredAsync(project.Key, [relative], cancellationToken)).GetValueOrDefault(relative);
        var hit = new CodeMapHit(relative, name,
            layer ?? stored?.Layer ?? CodeMapHeuristics.Layer(relative) ?? MemoryLayer.Logic,
            CodeMapHeuristics.AreaOf(relative, CodeMapHeuristics.AreaFolders(project)), CodeMapSource.Manual);
        await SaveAsync(project.Key, [hit], cancellationToken, overwriteManual: true);
        return hit;
    }
}
