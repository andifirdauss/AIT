using AiThread.Common.Memory;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    private async Task<string> KeyAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken)
    {
        if (knowledge) return MemoryLabels.KnowledgeKey;
        if (projectId is not { } id) throw new AppValidationException("Pilih project dulu.");
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var key = await db.Projects.AsNoTracking().Where(p => p.Id == id).Select(p => p.ProjectKey).FirstOrDefaultAsync(cancellationToken);
        if (key is null) throw new AppValidationException("Project tidak ditemukan.");
        if (key.Length == 0) throw new AppValidationException("Project belum punya key; buka lagi menu Project sebentar lagi.");
        return key;
    }
}
