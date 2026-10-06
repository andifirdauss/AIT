using AiThread.Common.Enums;

namespace AiThread.Common.Entities;

// One page of a project's business wiki (business memory): requirements, business rules, statuses and validations of
// one module, in Markdown for non-technical readers. Kept in the business memory store, which several developers may
// share, so it is keyed by Project.ProjectKey rather than by a local project id.
public class BusinessPage : BaseEntity
{
    public string ProjectKey { get; set; } = string.Empty;
    public string? ProjectName { get; set; }

    // Business module the page describes, e.g. "TO"; pages are grouped by it.
    public string Module { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    // Unique within the project key; derived from module and title.
    public string Slug { get; set; } = string.Empty;

    // Comma-separated keywords the planner scans, like a memory label.
    public string Labels { get; set; } = string.Empty;

    // One or two sentences shown in lists and to the wiki writer.
    public string? Summary { get; set; }

    // Markdown. Images are referenced as ![caption](asset:<BusinessAsset id>).
    public string Content { get; set; } = string.Empty;

    // Concurrency token: two developers (or the writer and an editor) never overwrite each other silently.
    public int Version { get; set; } = 1;

    // False = curated by hand; the periodic writer leaves the page alone.
    public bool AutoUpdate { get; set; } = true;

    public string? UpdatedBy { get; set; }
    public BusinessEditKind LastEditKind { get; set; } = BusinessEditKind.Manual;
}
