namespace AiThread.Common.Entities;

// A codebase seen by the proxy, uniquely identified by its root folder (the folder holding .git or a build manifest).
public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public ICollection<ThreadRun> Threads { get; set; } = [];
}
