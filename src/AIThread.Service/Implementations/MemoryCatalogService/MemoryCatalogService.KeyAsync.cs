using AIThread.Common.Localization;
using AIThread.Common.Memory;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    private async Task<string> KeyAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken)
    {
        if (knowledge) return MemoryLabels.KnowledgeKey;
        if (projectId is not { } id) throw new AppValidationException(Lang.T("Pilih project dulu.", "Choose a project first."));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var key = await db.Projects.AsNoTracking().Where(p => p.Id == id).Select(p => p.ProjectKey).FirstOrDefaultAsync(cancellationToken);
        if (key is null) throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        if (key.Length == 0) throw new AppValidationException(Lang.T("Project belum punya key; buka lagi menu Project sebentar lagi.", "The project has no key yet; open the Project menu again in a moment."));
        return key;
    }
}
