using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

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

// Asked once, the first time AIThread is opened, when git has no user.name either.
public record SetDeveloperNameRequest(string Name);

// A wiki page as it holds for one environment (BusinessEnvPage): Version = the working page's revision it shows,
// WorkingVersion = the working page's version now (null when the page was deleted). Content and Html only for one page.
public record BusinessEnvPageDto(
    Guid PageId,
    string Environment,
    string Module,
    string Title,
    string Slug,
    string? Summary,
    int Version,
    int? WorkingVersion,
    DateTime? UpdatedAt,
    string? Content = null,
    string? Html = null);

// The environment's pages; Missing = working pages with no version in the environment yet.
public record BusinessEnvOverviewDto(string Environment, IReadOnlyList<BusinessEnvPageDto> Pages, int Missing);

// One line of a page diff: Kind = "same", "add" (only right), "del" (only left), or "skip" (Text = how many unchanged
// lines are left out).
public record BusinessDiffLineDto(string Kind, string Text);

// Status = "same", "changed", "left" (only on the left side) or "right".
public record BusinessCompareItemDto(
    Guid PageId,
    string Module,
    string Title,
    string Status,
    int? LeftVersion,
    int? RightVersion,
    IReadOnlyList<BusinessDiffLineDto> Diff);

// A change (thread or ingested commit) on one side only: Side = "left" or "right".
public record BusinessCompareChangeDto(Guid ThreadId, string? Title, string? Author, string? Branch, string Side, string? Commit);

// Left and Right are environment names or "work" (the working wiki).
public record BusinessCompareDto(string Left, string Right, IReadOnlyList<BusinessCompareItemDto> Pages, IReadOnlyList<BusinessCompareChangeDto> Changes);
