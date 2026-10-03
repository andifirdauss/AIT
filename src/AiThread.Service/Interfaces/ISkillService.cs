using AiThread.Common.DTOs;

namespace AiThread.Service.Interfaces;

// The offline skill store: settings of the skill orchestrator, the sources skills are pulled from and the skills.
public interface ISkillService
{
    Task<SkillSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<SkillSettingsDto> UpdateSettingsAsync(UpdateSkillSettingsRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default);

    // Validates the link, stores the source and queues the pull.
    Task<SkillSourceDto> AddSourceAsync(AddSkillSourceRequest request, CancellationToken cancellationToken = default);

    Task<SkillSourceDto?> SyncSourceAsync(Guid id, CancellationToken cancellationToken = default);

    // Removes the source, its skills (and with them their place on nodes) and its folder.
    Task<bool> DeleteSourceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillDto>> GetSkillsAsync(string? query, CancellationToken cancellationToken = default);

    Task<SkillDetailDto?> GetSkillAsync(Guid id, CancellationToken cancellationToken = default);
}

// Picks skills from the store for what a developer wants a node to be good at.
public interface ISkillOrchestrator
{
    Task<IReadOnlyList<SkillSuggestionDto>> SuggestAsync(SuggestSkillsRequest request, CancellationToken cancellationToken = default);
}
