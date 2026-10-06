using AiThread.Common.Enums;

namespace AiThread.Common.DTOs;

// A business wiki page without its content (lists, planner index).
public record BusinessPageSummaryDto(
    Guid Id,
    string ProjectKey,
    string? ProjectName,
    string Module,
    string Title,
    string Slug,
    string? Summary,
    string Labels,
    int Version,
    bool AutoUpdate,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? UpdatedBy,
    BusinessEditKind LastEditKind);

// Content is the Markdown source; Html is it rendered (images point at /api/business/assets/{id}).
public record BusinessPageDto(BusinessPageSummaryDto Page, string Content, string Html);

// Version is the one the editor started from: saving over a newer version is refused instead of losing that change.
public record SaveBusinessPageRequest(
    Guid ProjectId,
    string Module,
    string Title,
    string? Summary,
    string? Labels,
    string Content,
    bool AutoUpdate = true,
    int? Version = null,
    string? ChangeNote = null);

public record BusinessRevisionDto(
    Guid Id,
    int Version,
    string Title,
    string? ChangeNote,
    string? Author,
    BusinessEditKind EditKind,
    DateTime CreatedAt,
    IReadOnlyList<Guid> SourceThreadIds,
    string? Content = null);

public record BusinessAssetDto(Guid Id, string MediaType, string? Caption, string Url, int Size);

// Data is the image as base64, or as a data: URL (what a pasted or picked file gives the browser).
public record UploadBusinessAssetRequest(Guid ProjectId, Guid? PageId, string Data, string? Caption);

public record PreviewBusinessPageRequest(string Content);

// What the Bisnis tab shows for one project.
public record BusinessOverviewDto(
    Guid ProjectId,
    string ProjectKey,
    string ProjectName,
    bool KeyIsShared,
    IReadOnlyList<BusinessPageSummaryDto> Pages,
    int PendingThreads,
    DateTime? LastDigestAt,
    bool DigestRunning,
    string StoreDescription);

public record BusinessDigestResultDto(
    int Threads,
    int PagesCreated,
    int PagesUpdated,
    int Images,
    IReadOnlyList<string> Warnings);

// DeveloperName is the one saved in the Setting menu; GitUserName is user.name from the global git config, used when
// none is saved. The first-run popup only asks when neither exists.
public record BusinessSettingsDto(
    bool Enabled,
    int IntervalHours,
    string? Model,
    string? DeveloperName,
    string EffectiveDeveloperName,
    string? GitUserName = null);

// DeveloperName empty = use the git user.name (or the Windows user).
public record UpdateBusinessSettingsRequest(bool Enabled, int IntervalHours, string? Model, string? DeveloperName);

// Asked once, the first time AiThread is opened, when git has no user.name either.
public record SetDeveloperNameRequest(string Name);
