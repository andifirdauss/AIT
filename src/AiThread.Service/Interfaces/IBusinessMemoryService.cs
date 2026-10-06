using AiThread.Common.DTOs;

namespace AiThread.Service.Interfaces;

// The business wiki (business memory) of a project: pages per business module, kept in the business memory store and
// shared by every clone of the project through its key. Project ids are this machine's; the service maps them to keys.
public interface IBusinessMemoryService
{
    Task<BusinessOverviewDto?> GetOverviewAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<BusinessPageDto?> GetPageAsync(Guid id, CancellationToken cancellationToken = default);

    Task<BusinessPageDto> CreatePageAsync(SaveBusinessPageRequest request, CancellationToken cancellationToken = default);

    // Refused (BusinessPageConflictException) when the page changed since request.Version.
    Task<BusinessPageDto?> UpdatePageAsync(Guid id, SaveBusinessPageRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeletePageAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessRevisionDto>> GetRevisionsAsync(Guid pageId, CancellationToken cancellationToken = default);

    Task<BusinessRevisionDto?> GetRevisionAsync(Guid pageId, int version, CancellationToken cancellationToken = default);

    Task<BusinessPageDto?> RestoreAsync(Guid pageId, int version, CancellationToken cancellationToken = default);

    Task<BusinessAssetDto> AddAssetAsync(Guid projectId, Guid? pageId, byte[] data, string? caption, CancellationToken cancellationToken = default);

    Task<(string MediaType, byte[] Data)?> GetAssetAsync(Guid id, CancellationToken cancellationToken = default);

    // Renders page Markdown the way a saved page is shown (for the editor preview).
    string Render(string markdown);

    // Pages of the project's key, without content (planner memory index).
    Task<IReadOnlyList<BusinessPageSummaryDto>> GetPagesAsync(Guid projectId, CancellationToken cancellationToken = default);

    // Pages as the planner reads them: images replaced by their captions.
    Task<IReadOnlyList<(BusinessPageSummaryDto Page, string Content)>> GetForPlannerAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<BusinessSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<BusinessSettingsDto> UpdateSettingsAsync(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken = default);

    // The first-run popup: records who works on this machine, keeping the other business memory settings.
    Task<BusinessSettingsDto> SetDeveloperNameAsync(string name, CancellationToken cancellationToken = default);
}
