namespace AIThread.Common.Entities;

// A multi-step test of a project's running app (submit, then approve per level, edit before approval, ...), made from a
// tester's suggestion and completed by a developer in an Excel sheet AIT generates. Shared by the team through
// ProjectKey, next to the test accounts. Steps, Roles and Fields are JSON; LastRun is the TestingReport of its latest run.
public class TestingScenario : BaseEntity
{
    public const int MaxTitleLength = 300;
    public const int MaxSlugLength = 80;

    public string ProjectKey { get; set; } = string.Empty;

    // File name of its sheet (.aithread/testing/<slug>.xlsx), unique per project.
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Goes up on every import of its sheet.
    public int Version { get; set; } = 1;

    // ui or api, and the app's root URL, from the thread it came from.
    public string Level { get; set; } = "ui";
    public string? BaseUrl { get; set; }
    public string? Notes { get; set; }

    public string Steps { get; set; } = "[]";
    public string Roles { get; set; } = "[]";
    public string Fields { get; set; } = "[]";

    // The thread whose test it continues (its plan, changes and remembered values), and the suggestion it came from.
    public Guid? SourceThreadId { get; set; }
    public int? SourceFollowUp { get; set; }

    public string? LastRun { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastRunBy { get; set; }

    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
