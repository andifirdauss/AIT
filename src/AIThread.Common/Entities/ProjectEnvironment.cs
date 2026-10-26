namespace AIThread.Common.Entities;

// A deployment environment of a project (dev, staging, prod, ...) and the git ref that stands for it, e.g. "prod" =
// "origin/main". Shared by every developer of the project key, next to long memory: one person sets it up, every AIT
// uses it. GitEnvironmentTracker records which thread's change reached which environment (ChangePresence), and the
// business wiki keeps a page per environment (BusinessEnvPage).
public class ProjectEnvironment : BaseEntity
{
    public const int MaxNameLength = 50;
    public const int MaxRefLength = 250;

    public string ProjectKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;

    // Display order, e.g. dev 1, staging 2, prod 3.
    public int Order { get; set; }
    public bool IsProduction { get; set; }

    public string? UpdatedBy { get; set; }
}
