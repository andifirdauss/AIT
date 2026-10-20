using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// The module list, code map, module merges and review queue behind the Memory and Project menus. knowledge = the
// modules of knowledge memory instead of a project's.
public interface IMemoryCatalogService
{
    Task<IReadOnlyList<ProjectModuleDto>> GetModulesAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken = default);

    Task<ProjectModuleDto?> UpdateModuleAsync(Guid? projectId, bool knowledge, string name, SaveModuleRequest request, CancellationToken cancellationToken = default);

    Task<ModuleMergeDto> MergeAsync(MergeModulesRequest request, bool knowledge, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModuleMergeDto>> GetMergesAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken = default);

    Task UndoMergeAsync(Guid id, CancellationToken cancellationToken = default);

    Task AcceptMergeAsync(Guid id, CancellationToken cancellationToken = default);

    Task RejectMergeAsync(Guid id, CancellationToken cancellationToken = default);

    // Looks for duplicate modules now. Returns merges applied plus suggestions made.
    Task<int> CheckModulesAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodeMapEntryDto>> GetCodeMapAsync(Guid projectId, string? search, string? module, CancellationToken cancellationToken = default);

    Task<CodeMapEntryDto> SetCodeMapAsync(Guid projectId, SetCodeMapRequest request, CancellationToken cancellationToken = default);

    Task<CodeMapScanResultDto> ScanCodeMapAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<MemoryReviewDto> GetReviewAsync(Guid? projectId, CancellationToken cancellationToken = default);

    Task<MemoryRelabelStatusDto> GetRelabelStatusAsync(CancellationToken cancellationToken = default);

    // Starts a relabel pass in the background (no-op while one runs).
    void StartRelabel();
}
