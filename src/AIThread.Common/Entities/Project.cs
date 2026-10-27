using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// A codebase seen by the proxy, uniquely identified by its root folder (the folder holding .git or a build manifest).
public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;

    // Identity shared by every clone of the repository, on any machine: memory is scoped by it, so developers whose
    // root folders differ still build one memory per project. Recomputed whenever the project is seen.
    public string ProjectKey { get; set; } = string.Empty;
    public ProjectKeySource ProjectKeySource { get; set; } = ProjectKeySource.Local;

    // Key typed in the Project menu, for repositories without a remote. .aithread/project.json still wins.
    public string? ManualKey { get; set; }

    public string? Summary { get; set; }

    // Minutes between automatic git fetches of the project's remotes (GitChangeTracker), so pushes and merges of other
    // developers show; null = the default (GitChangeTracker.DefaultFetchMinutes), 0 = never.
    public int? GitFetchMinutes { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public ICollection<ThreadRun> Threads { get; set; } = [];
}
