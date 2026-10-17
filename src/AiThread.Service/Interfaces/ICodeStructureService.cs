using AiThread.Common.DTOs;

namespace AiThread.Service.Interfaces;

// Folder and class tree of a project for Project > Detail. The project is walked on first use and again when the last
// walk is older than a minute (or on rescan); unchanged files keep their outline.
public interface ICodeStructureService
{
    Task<CodeStructureSummaryDto> GetSummaryAsync(Guid projectId, bool rescan, CancellationToken cancellationToken = default);

    // path: a folder relative to the root; empty for the root.
    Task<StructureFolderDto> GetFolderAsync(Guid projectId, string? path, CancellationToken cancellationToken = default);

    Task<StructureFileDto> GetFileAsync(Guid projectId, string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StructureGroupDto>> GetGroupsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StructureTypeSummaryDto>> GetTypesAsync(Guid projectId, string group, CancellationToken cancellationToken = default);

    Task<StructureClassDto> GetClassAsync(Guid projectId, string group, string name, string? container, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StructureSearchHitDto>> SearchAsync(Guid projectId, string query, CancellationToken cancellationToken = default);

    // After a thread or a rollback changed files: walks the project again in the background, so the tree is current
    // when it is opened. Never blocks or fails the caller; requests made while one is waiting share it.
    void Refresh(Guid projectId);
}
