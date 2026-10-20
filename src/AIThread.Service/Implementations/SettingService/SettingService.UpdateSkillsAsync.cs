using AIThread.Common.DTOs;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    public async Task<RuntimeSettings> UpdateSkillsAsync(UpdateSkillSettingsRequest request, CancellationToken cancellationToken = default)
    {
        EnsureRange(request.MaxPromptChars, 4_000, 200_000, "Batas karakter skill per node");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);
        entity.SkillOrchestratorModel = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        entity.SkillPromptMaxChars = request.MaxPromptChars;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return _cache;
    }
}
