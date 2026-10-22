using AIThread.Common.Enums;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Business;

// A page as it should be after a save. Id null creates the page; ExpectedVersion is the version the change was made
// from (null = no check, used only when creating).
public sealed record BusinessPageDraft(
    Guid? Id,
    string ProjectKey,
    string? ProjectName,
    string Module,
    string Title,
    string? Summary,
    string Labels,
    string Content,
    bool AutoUpdate,
    int? ExpectedVersion,
    BusinessEditKind EditKind,
    string Author,
    string? ChangeNote,
    IReadOnlyList<Guid> SourceThreadIds);

// Saving business pages and their images. Every save writes a revision; a page changed by someone else since it was
// read is never overwritten (BusinessPageConflictException), since several developers may share the store.
// The module is the one of the project's module list (shared with memory labels) and the labels are PascalCase.
// The text of a page is saved with its secrets masked (SecretGuard): the wiki is shared and read by the planner.
public sealed partial class BusinessPageStore(BusinessMemoryStore store, IModuleRegistry registry, ISecretGuard secrets, ILogger<BusinessPageStore> logger) : IBusinessPageStore
{
    public const int MaxContentChars = 200_000;
}
